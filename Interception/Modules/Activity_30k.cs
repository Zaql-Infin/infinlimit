using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

using InfinLimit.Models;

namespace InfinLimit.Interception.Modules
{
    /// <summary>
    /// Auto-arms when a new 30000 SYN is detected and queues/delays inbound packets
    /// for a configurable window, allowing loot/chest interactions to complete before
    /// traffic resumes. Automatically stops after ActivityRunSeconds.
    /// </summary>
    public class Activity_30k : PacketModuleBase
    {
        private const int MaxPacketsPerTick = 96;

        public static int ActivityRunSeconds = 24;
        public static int QueueDelaySeconds = 12;

        private static readonly TimeSpan IdleGap = TimeSpan.FromSeconds(10.0);

        private readonly ConcurrentQueue<Packet> _queue = new ConcurrentQueue<Packet>();
        private readonly object _detectLock = new object();
        private Timer _timer;
        private int _processingFlag;
        private int _autoStopFlag;

        private DateTime _activationTimeUtc;
        private string _activeRemoteKey;
        private string _lastArmedRemoteKey = string.Empty;
        private volatile bool _awaitFinRstForRearm;
        private volatile bool _firstPayloadPassed;

        public static bool Buffer = true;
        public static bool DetectionEnabled = true;
        public static volatile bool Armed = false;
        public static volatile int ElapsedSeconds = 0;
        public static volatile int RemainingSeconds = 0;
        public static volatile int TotalSeconds = 24;

        private volatile bool _enabled = true;

        private static long _cooldownUntilUtcTicks;
        public static long IgnoreRemoteChangeUntilUtcTicks;
        public static long SuppressActivityUntilUtcTicks;
        public static long DoNotArmUntilUtcTicks;
        public static long ReconnectBaselineCaptureUntilUtcTicks;
        public static string ReconnectBaselineRemoteKey = string.Empty;

        private static readonly object ReconnectBaselineLock = new object();
        private static readonly ConcurrentDictionary<string, byte> StartupExistingRemotes = new ConcurrentDictionary<string, byte>();
        public static long StartupExistingRemotesIgnoreUntilUtcTicks;

        private static DateTime _activitySkipLast30kLikeSeenUtc = DateTime.MinValue;

        private static readonly object _activityLogLock = new object();
        private static readonly string _activityLogPath = Path.Combine(App.ExeDirectory, "activity_debug.log");

        private static bool InCooldown() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _cooldownUntilUtcTicks);
        private static void StartCooldown() => Interlocked.Exchange(ref _cooldownUntilUtcTicks, DateTime.UtcNow.Add(IdleGap).Ticks);

        public static void IgnoreRemoteChangesForSeconds(int seconds) =>
            Interlocked.Exchange(ref IgnoreRemoteChangeUntilUtcTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks);
        private static bool IsIgnoringRemoteChanges() =>
            DateTime.UtcNow.Ticks < Interlocked.Read(ref IgnoreRemoteChangeUntilUtcTicks);

        public static void SuppressForSeconds(int seconds) =>
            Interlocked.Exchange(ref SuppressActivityUntilUtcTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks);
        private static bool IsSuppressed() =>
            DateTime.UtcNow.Ticks < Interlocked.Read(ref SuppressActivityUntilUtcTicks);

        public static void DoNotArmForSeconds(int seconds) =>
            Interlocked.Exchange(ref DoNotArmUntilUtcTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks);
        private static bool IsArmLockedOut() =>
            DateTime.UtcNow.Ticks < Interlocked.Read(ref DoNotArmUntilUtcTicks);

        public static void BeginReconnectBaselineCapture(int seconds)
        {
            Interlocked.Exchange(ref ReconnectBaselineCaptureUntilUtcTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks);
            lock (ReconnectBaselineLock) { ReconnectBaselineRemoteKey = string.Empty; }
        }
        private static bool IsReconnectBaselineCaptureActive() =>
            DateTime.UtcNow.Ticks < Interlocked.Read(ref ReconnectBaselineCaptureUntilUtcTicks);

        private static bool IsWithinStartupExistingRemoteGuard() =>
            DateTime.UtcNow.Ticks < Interlocked.Read(ref StartupExistingRemotesIgnoreUntilUtcTicks);

        private void CaptureStartupExistingRemotes()
        {
            try
            {
                StartupExistingRemotes.Clear();
                var provider = PacketProviders.FirstOrDefault();
                if (provider == null) return;
                foreach (var key in provider.Connections.Keys)
                    StartupExistingRemotes.TryAdd(key, 0);
                Interlocked.Exchange(ref StartupExistingRemotesIgnoreUntilUtcTicks, DateTime.UtcNow.AddSeconds(15).Ticks);
            }
            catch { }
        }

        public void ForceStop()
        {
            if (IsActivated) StopAndFlush();
        }

        public Activity_30k()
            : base("Activity", true, InterceptionManager.GetProvider("30000"))
        {
            Icon = System.Windows.Application.Current.TryFindResource("RocketIcon") as Geometry
                ?? System.Windows.Application.Current.FindResource("DefaultIcon") as Geometry;
            Description = "Auto-arms on new 30k connection (SYN detection)\nQueues inbound packets for the activity window\nAuto-stops after configured seconds";

            var named = Config.GetNamed(Name);
            try { Buffer = named.GetSettings<bool>("Buffer"); } catch { }
            try
            {
                ActivityRunSeconds = named.GetSettings<int>("ActivityRunSeconds");
                if (ActivityRunSeconds < 1) ActivityRunSeconds = 1;
            }
            catch { }
            try
            {
                QueueDelaySeconds = named.GetSettings<int>("QueueDelaySeconds");
                if (QueueDelaySeconds < 0) QueueDelaySeconds = 0;
            }
            catch { }
            try
            {
                if (named.Settings != null && named.Settings.ContainsKey("DetectionEnabled"))
                    DetectionEnabled = named.GetSettings<bool>("DetectionEnabled");
            }
            catch { DetectionEnabled = true; }

            Togglable = false;
            EnsureTimer();
            CaptureStartupExistingRemotes();
        }

        public override void Toggle()
        {
            _enabled = !_enabled;
            EnsureTimer();
            if (!_enabled)
            {
                MaybeReset30kInstanceTimer();
                if (IsActivated) StopAndFlush();
                else ClearQueueOnly();
                lock (_detectLock) { _activeRemoteKey = null; }
                _lastArmedRemoteKey = string.Empty;
                _awaitFinRstForRearm = false;
                Armed = false;
                ElapsedSeconds = 0;
                RemainingSeconds = 0;
            }
            else
            {
                lock (_detectLock) { _activeRemoteKey = null; }
                _firstPayloadPassed = false;
                Interlocked.Exchange(ref _autoStopFlag, 0);
                Interlocked.Exchange(ref _processingFlag, 0);
                StartCooldown();
            }
        }

        public override void StopListening()
        {
            ClearQueueOnly();
            base.StopListening();
        }

        private void EnsureTimer()
        {
            if (_timer == null)
                _timer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        }

        private void ClearQueueOnly()
        {
            Packet p;
            while (_queue.TryDequeue(out p)) { }
        }

        private static string GetRemoteKey(Packet p)
        {
            if (p.SrcPort == 30000)
                return $"{p.SrcAddr}:{p.SrcPort}";
            return $"{p.DstAddr}:{p.DstPort}";
        }

        private void ArmNow()
        {
            if (!_enabled) { LogActivity("ARMNOW_BLOCK disabled"); return; }
            if (!DetectionEnabled) { LogActivity("ARMNOW_BLOCK detection_off"); return; }
            if (IsSuppressed()) { LogActivity("ARMNOW_BLOCK suppressed"); return; }
            if (IsActivity30kSkipActive()) { LogActivity("ARMNOW_BLOCK reconnect_30k_inf_skip"); return; }
            if (IsArmLockedOut()) { LogActivity("ARMNOW_BLOCK arm_lockout"); return; }
            if (IsActivated) { LogActivity("ARMNOW_BLOCK already_active"); return; }

            _activationTimeUtc = DateTime.UtcNow;
            _firstPayloadPassed = false;
            Interlocked.Exchange(ref _autoStopFlag, 0);
            Armed = true;
            ElapsedSeconds = 0;
            TotalSeconds = ActivityRunSeconds;
            RemainingSeconds = TotalSeconds;
            StartTime = DateTime.Now;
            IsActivated = true;
            _lastArmedRemoteKey = _activeRemoteKey ?? string.Empty;
            _awaitFinRstForRearm = true;
            LogActivity($"ARMNOW_ACTIVATED active={_activeRemoteKey} runSec={ActivityRunSeconds} queueSec={QueueDelaySeconds}");
        }

        private void StopAndFlush()
        {
            StartCooldown();
            IsActivated = false;
            lock (_detectLock)
            {
                if (!IsIgnoringRemoteChanges()) _activeRemoteKey = null;
            }
            Armed = false;
            ElapsedSeconds = 0;
            RemainingSeconds = 0;
            TotalSeconds = ActivityRunSeconds;
            Packet p;
            while (_queue.TryDequeue(out p))
            {
                try
                {
                    p.Delayed = false;
                    p.AckNum = 0;
                    p.SourceProvider.StorePacket(p);
                    if (Buffer && !p.Flags.HasFlag(TcpFlags.FIN) && !p.Flags.HasFlag(TcpFlags.RST))
                        p.SourceProvider.SendPacket(p, force: true).Wait(TimeSpan.FromMilliseconds(250));
                }
                catch { }
            }
            MaybeReset30kInstanceTimer();
        }

        private static void MaybeReset30kInstanceTimer()
        {
            if (IsActivity30kSkipActive()) return;
            // InfinLimiter's _30000_Provider doesn't have an instance timer — nothing to reset.
        }

        private static bool IsReconnectRecentlyTriggered() =>
            DateTime.UtcNow - ReconnectModule.LastReconnectUtc <= TimeSpan.FromSeconds(6);

        private static bool IsActivity30kSkipActive()
        {
            DateTime now = DateTime.UtcNow;
            bool reconnectRecent = IsReconnectRecentlyTriggered();

            bool instanceLike = false;
            try
            {
                // InstanceModule (З0K) is the nearest equivalent to _30k in InfinLimiter
                var instance = InterceptionManager.Modules.OfType<InstanceModule>().FirstOrDefault();
                instanceLike = instance?.IsActivated ?? false;
            }
            catch { }

            if (instanceLike) _activitySkipLast30kLikeSeenUtc = now;

            bool recent = _activitySkipLast30kLikeSeenUtc != DateTime.MinValue
                && now - _activitySkipLast30kLikeSeenUtc <= TimeSpan.FromSeconds(10);

            return reconnectRecent || instanceLike || recent;
        }

        public static bool IsReconnect30kInfSkipActiveForGuards() => IsActivity30kSkipActive();

        public static bool ShouldIgnoreRemoteForShipSkip(string remoteKey)
        {
            if (string.IsNullOrWhiteSpace(remoteKey)) return true;
            if (IsReconnectBaselineCaptureActive())
            {
                lock (ReconnectBaselineLock)
                {
                    if (string.IsNullOrWhiteSpace(ReconnectBaselineRemoteKey))
                        ReconnectBaselineRemoteKey = remoteKey;
                    if (string.Equals(ReconnectBaselineRemoteKey, remoteKey, StringComparison.Ordinal))
                        return true;
                }
            }
            else
            {
                lock (ReconnectBaselineLock)
                {
                    if (!string.IsNullOrWhiteSpace(ReconnectBaselineRemoteKey)
                        && string.Equals(ReconnectBaselineRemoteKey, remoteKey, StringComparison.Ordinal))
                        return true;
                }
            }
            if (IsWithinStartupExistingRemoteGuard())
                return StartupExistingRemotes.ContainsKey(remoteKey);
            return false;
        }

        private static void LogActivity(string message)
        {
            try
            {
                lock (_activityLogLock)
                    File.AppendAllText(_activityLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void MaybeArmOnActivity(Packet p, string remoteKey)
        {
            if (!p.Flags.HasFlag(TcpFlags.SYN) || p.Flags.HasFlag(TcpFlags.ACK)
                || (p.DstPort != 30000 && p.SrcPort != 30000))
                return;

            if (!_enabled) { LogActivity("ARM_BLOCK disabled key=" + remoteKey); return; }
            if (!DetectionEnabled) { LogActivity("ARM_BLOCK detection_off key=" + remoteKey); return; }
            if (IsSuppressed()) { LogActivity("ARM_BLOCK suppressed key=" + remoteKey); return; }
            if (InCooldown()) { LogActivity("ARM_BLOCK cooldown key=" + remoteKey); return; }
            if (IsArmLockedOut()) { LogActivity("ARM_BLOCK arm_lockout key=" + remoteKey); return; }
            if (_awaitFinRstForRearm)
            {
                LogActivity("ARM_BLOCK await_fin_rst key=" + remoteKey + " last=" + _lastArmedRemoteKey);
                return;
            }

            lock (_detectLock) { _activeRemoteKey = remoteKey; }
            LogActivity($"ARM_OK key={remoteKey} src={p.SrcAddr}:{p.SrcPort} dst={p.DstAddr}:{p.DstPort}");
            ArmNow();
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;
            if (!_enabled) return true;
            if (p.SrcPort != 30000 && p.DstPort != 30000) return true;

            string remoteKey = GetRemoteKey(p);

            if (_awaitFinRstForRearm
                && (p.Flags.HasFlag(TcpFlags.FIN) || p.Flags.HasFlag(TcpFlags.RST))
                && (string.IsNullOrEmpty(_lastArmedRemoteKey) || string.Equals(remoteKey, _lastArmedRemoteKey, StringComparison.Ordinal)))
            {
                _awaitFinRstForRearm = false;
                LogActivity("REARM_UNLOCK finrst key=" + remoteKey);
            }

            MaybeArmOnActivity(p, remoteKey);

            if (!IsActivated) return true;

            string activeKey;
            lock (_detectLock) { activeKey = _activeRemoteKey; }

            if (activeKey == null || !string.Equals(activeKey, remoteKey, StringComparison.Ordinal))
                return true;

            if (p.Flags.HasFlag(TcpFlags.SYN) || p.Flags.HasFlag(TcpFlags.FIN) || p.Flags.HasFlag(TcpFlags.RST))
                return true;

            if (p.Length == 0) return true;

            if (!_firstPayloadPassed) { _firstPayloadPassed = true; return true; }

            p.CreatedAt = DateTime.UtcNow;
            _queue.Enqueue(p);
            return false;
        }

        private void ProcessQueue(object _)
        {
            if (!IsActivated || Interlocked.Exchange(ref _processingFlag, 1) == 1) return;
            try
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _activationTimeUtc).TotalSeconds >= ActivityRunSeconds)
                {
                    if (Interlocked.Exchange(ref _autoStopFlag, 1) == 0) StopAndFlush();
                    return;
                }

                TimeSpan elapsed = now - _activationTimeUtc;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                ElapsedSeconds = (int)Math.Floor(elapsed.TotalSeconds);
                if (ElapsedSeconds > ActivityRunSeconds) ElapsedSeconds = ActivityRunSeconds;
                double remaining = ActivityRunSeconds - elapsed.TotalSeconds;
                if (remaining < 0) remaining = 0;
                RemainingSeconds = (int)Math.Ceiling(remaining);

                int sent = 0;
                Packet peek;
                while (_queue.TryPeek(out peek) && sent < MaxPacketsPerTick
                    && !((now - peek.CreatedAt).TotalSeconds < QueueDelaySeconds))
                {
                    if (!_queue.TryDequeue(out Packet p)) continue;
                    try
                    {
                        p.Delayed = true;
                        p.AckNum = 0;
                        p.SourceProvider.StorePacket(p);
                        if (!p.SourceProvider.SendPacket(p, force: true).Wait(TimeSpan.FromMilliseconds(250)))
                        {
                            StopAndFlush();
                            break;
                        }
                        sent++;
                    }
                    catch { StopAndFlush(); break; }
                }
            }
            finally { Interlocked.Exchange(ref _processingFlag, 0); }
        }
    }
}
