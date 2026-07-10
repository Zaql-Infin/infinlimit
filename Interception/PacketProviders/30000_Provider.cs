using InfinLimit.Interception.Modules;
using InfinLimit.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using WindivertDotnet;

namespace InfinLimit.Interception.PacketProviders
{
    public class _30000_Provider : PacketProviderBase
    {
        private static long _packetCount = 0L;

        private readonly object _timerLock = new object();

        private static readonly TimeSpan IdleGap = TimeSpan.FromSeconds(2.0);

        private ushort? _activeLocalPort;
        private DateTime _lastPacketUtc = DateTime.MinValue;
        private DateTime _instanceStartUtc = DateTime.MinValue;

        private readonly Dictionary<ushort, (DateTime Start, DateTime LastSeen)> _connectionStarts
            = new Dictionary<ushort, (DateTime, DateTime)>();

        private static readonly TimeSpan ConnectionStaleAfter = TimeSpan.FromSeconds(6.0);

        private string? _activeRemoteKey;
        private DateTime _lastSynStartUtc = DateTime.MinValue;
        private string? _lastSynStartRemoteKey;

        private DateTime _pendingDisconnectUntilUtc = DateTime.MinValue;
        private DateTime _instanceStartBeforeDisconnectUtc = DateTime.MinValue;
        private string? _fingerprintBeforeDisconnect;
        private string? _remoteIpBeforeDisconnect;
        private bool _preserveStartAcrossReconnect;

        private DateTime _suppressRolloverResetUntilUtc = DateTime.MinValue;
        private DateTime _preserveSessionUntilUtc = DateTime.MinValue;

        private static readonly TimeSpan RejoinDecisionWindow = TimeSpan.FromSeconds(10.0);
        private static readonly TimeSpan PreserveRolloverGuard = TimeSpan.FromSeconds(5.0);

        private static readonly object _debugLogLock = new object();
        private static readonly string _debugLogPath = Path.Combine(App.ExeDirectory, "30000_timer_debug.log");

        public static long PacketCount => Interlocked.Read(ref _packetCount);
        public bool HasActiveInstance => InstanceDuration() != TimeSpan.Zero;

        public _30000_Provider() : base("30000", 30000, 30009, true)
        {
            BufferSeconds = 20;
        }

        protected override WinDivert CreateInstance()
        {
            return Divert = new WinDivert(
                Filter.True.And(x => x.IsTcp && x.Network.RemotePort >= 30000 && x.Network.RemotePort <= 30009),
                WinDivertLayer.Network, 0, WinDivertFlag.None);
        }

        private static bool Is30000Range(ushort port) => port >= 30000 && port <= 30009;

        private static string? ExtractIpFromRemoteKey(string? remoteKey)
        {
            if (string.IsNullOrWhiteSpace(remoteKey)) return null;
            int idx = remoteKey.LastIndexOf(':');
            return idx > 0 ? remoteKey.Substring(0, idx) : null;
        }

        private static string GetRemoteKey(Packet p)
        {
            if (p.Inbound)
                return $"{p.SrcAddr?.ToString() ?? "unknown"}:{p.SrcPort}";
            return $"{p.DstAddr?.ToString() ?? "unknown"}:{p.DstPort}";
        }

        private static bool ReconnectPulseActive()
            => DateTime.UtcNow - ReconnectModule.LastReconnectUtc <= TimeSpan.FromSeconds(12.0);

        private static bool HoldInstanceTimerDuringIdle()
        {
            try
            {
                var inst = InterceptionManager.Modules.OfType<InstanceModule>().FirstOrDefault();
                if (inst != null && inst.IsActivated) return true;
            }
            catch { }
            return false;
        }

        private static string? Get3074SessionFingerprint()
        {
            try
            {
                var provider = InterceptionManager.GetProvider("Xbox");
                if (provider?.Connections == null || provider.Connections.Count == 0) return null;

                Packet? best = null;
                foreach (var kv in provider.Connections)
                {
                    var last = kv.Value?.LastOrDefault();
                    if (last != null && (best == null || last.CreatedAt > best.CreatedAt))
                        best = last;
                }
                if (best == null) return null;
                var ip    = best.Inbound ? best.SrcAddr?.ToString() : best.DstAddr?.ToString() ?? "unknown";
                var port  = best.Inbound ? best.SrcPort : best.DstPort;
                return $"{ip}:{port}";
            }
            catch { return null; }
        }

        private void RememberConnectionStartUnsafe(ushort localPort, DateTime startUtc, DateTime nowUtc)
        {
            _connectionStarts[localPort] = (startUtc, nowUtc);
            if (_connectionStarts.Count <= 8) return;
            foreach (var key in _connectionStarts
                .Where(kv => nowUtc - kv.Value.LastSeen > ConnectionStaleAfter)
                .Select(kv => kv.Key).ToList())
            {
                _connectionStarts.Remove(key);
            }
        }

        private DateTime? TryGetConcurrentStartUnsafe(ushort localPort, DateTime nowUtc)
        {
            if (_connectionStarts.TryGetValue(localPort, out var v) && nowUtc - v.LastSeen <= IdleGap)
                return v.Start;
            return null;
        }

        private string SnapshotUnsafe()
            => $"start={_instanceStartUtc:O} last={_lastPacketUtc:O} pendingUntil={_pendingDisconnectUntilUtc:O} " +
               $"preStart={_instanceStartBeforeDisconnectUtc:O} preserve={_preserveStartAcrossReconnect} " +
               $"preserveUntil={_preserveSessionUntilUtc:O} rolloverGuardUntil={_suppressRolloverResetUntilUtc:O} " +
               $"remote={_activeRemoteKey ?? ""} lport={_activeLocalPort?.ToString() ?? ""}";

        private void DebugLog(string tag, string msg)
        {
            try
            {
                lock (_debugLogLock)
                    File.AppendAllText(_debugLogPath, $"[{DateTime.UtcNow:O}] {tag} {msg}{Environment.NewLine}");
            }
            catch { }
        }

        public override void StorePacket(Packet p)
        {
            base.StorePacket(p);

            if (!Is30000Range(p.SrcPort) && !Is30000Range(p.DstPort)) return;

            DateTime nowUtc   = DateTime.UtcNow;
            string remoteKey  = GetRemoteKey(p);
            ushort localPort  = p.Inbound ? p.DstPort : p.SrcPort;

            lock (_timerLock)
            {
                // Arm pending-disconnect window if preserve flag just set
                if (_preserveStartAcrossReconnect
                    && _instanceStartBeforeDisconnectUtc != DateTime.MinValue
                    && _pendingDisconnectUntilUtc == DateTime.MinValue)
                {
                    _pendingDisconnectUntilUtc = nowUtc.Add(RejoinDecisionWindow);
                }

                // Expire pending-disconnect
                if (_pendingDisconnectUntilUtc != DateTime.MinValue && nowUtc > _pendingDisconnectUntilUtc)
                {
                    if (_preserveStartAcrossReconnect)
                        _pendingDisconnectUntilUtc = nowUtc.Add(RejoinDecisionWindow);
                    else
                    {
                        _pendingDisconnectUntilUtc        = DateTime.MinValue;
                        _instanceStartBeforeDisconnectUtc = DateTime.MinValue;
                        _fingerprintBeforeDisconnect       = null;
                        _remoteIpBeforeDisconnect          = null;
                    }
                }

                bool isFinRst = p.Flags.HasFlag(TcpFlags.FIN) || p.Flags.HasFlag(TcpFlags.RST);
                bool isSyn    = p.Flags.HasFlag(TcpFlags.SYN) && !p.Flags.HasFlag(TcpFlags.ACK);

                if (isFinRst || isSyn)
                {
                    if (HoldInstanceTimerDuringIdle())
                    {
                        _pendingDisconnectUntilUtc        = nowUtc.Add(RejoinDecisionWindow);
                        _instanceStartBeforeDisconnectUtc = _instanceStartUtc;
                        _fingerprintBeforeDisconnect       = Get3074SessionFingerprint();
                        _remoteIpBeforeDisconnect          = ExtractIpFromRemoteKey(_activeRemoteKey) ?? ExtractIpFromRemoteKey(remoteKey);
                        _preserveStartAcrossReconnect      = _instanceStartUtc != DateTime.MinValue;
                        _activeLocalPort  = localPort;
                        _activeRemoteKey  = remoteKey;
                        if (isSyn)
                        {
                            _lastSynStartRemoteKey = remoteKey;
                            _lastSynStartUtc       = nowUtc;
                        }
                        _lastPacketUtc = nowUtc;
                        RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                        DebugLog("STORE", "hold rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if (ReconnectPulseActive())
                    {
                        _activeLocalPort = localPort;
                        _activeRemoteKey = remoteKey;
                        if (isSyn)
                        {
                            _lastSynStartRemoteKey = remoteKey;
                            _lastSynStartUtc       = nowUtc;
                        }
                        _lastPacketUtc = nowUtc;
                        RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                        DebugLog("STORE", "reconnect-pulse rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if (isFinRst)
                    {
                        bool sessionPreserved =
                            _preserveSessionUntilUtc != DateTime.MinValue && nowUtc <= _preserveSessionUntilUtc;
                        bool preserving =
                            (_preserveStartAcrossReconnect && _instanceStartBeforeDisconnectUtc != DateTime.MinValue)
                            || sessionPreserved;

                        _pendingDisconnectUntilUtc = nowUtc.Add(RejoinDecisionWindow);

                        if (!preserving)
                        {
                            _instanceStartBeforeDisconnectUtc = _instanceStartUtc;
                            _fingerprintBeforeDisconnect       = Get3074SessionFingerprint();
                            _remoteIpBeforeDisconnect          = ExtractIpFromRemoteKey(_activeRemoteKey) ?? ExtractIpFromRemoteKey(remoteKey);
                        }
                        else
                        {
                            if (_instanceStartBeforeDisconnectUtc == DateTime.MinValue)
                                _instanceStartBeforeDisconnectUtc = _instanceStartUtc;
                            if (string.IsNullOrWhiteSpace(_fingerprintBeforeDisconnect))
                                _fingerprintBeforeDisconnect = Get3074SessionFingerprint();
                            if (string.IsNullOrWhiteSpace(_remoteIpBeforeDisconnect))
                                _remoteIpBeforeDisconnect = ExtractIpFromRemoteKey(_activeRemoteKey) ?? ExtractIpFromRemoteKey(remoteKey);
                            _preserveSessionUntilUtc = nowUtc.Add(RejoinDecisionWindow);
                        }

                        _preserveStartAcrossReconnect = preserving;
                        _activeLocalPort   = null;
                        _activeRemoteKey   = null;
                        _instanceStartUtc  = DateTime.MinValue;
                        _lastPacketUtc     = DateTime.MinValue;
                        DebugLog("CTRL", $"fin/rst rk={remoteKey} preserveArmed={preserving} state={SnapshotUnsafe()}");
                    }
                    else // SYN (new connection)
                    {
                        _activeLocalPort   = localPort;
                        _activeRemoteKey   = remoteKey;
                        _lastSynStartRemoteKey = remoteKey;
                        _lastSynStartUtc   = nowUtc;
                        _instanceStartUtc  = ResolveStartUtc(nowUtc, remoteKey);
                        _lastPacketUtc     = nowUtc;
                        RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                        DebugLog("STORE", "syn-new rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                }
                else if (_activeLocalPort != localPort)
                {
                    DateTime? prevStart = TryGetConcurrentStartUnsafe(localPort, nowUtc);
                    bool idleActive = _instanceStartUtc != DateTime.MinValue
                        && _lastPacketUtc != DateTime.MinValue
                        && nowUtc - _lastPacketUtc <= IdleGap;

                    _activeLocalPort = localPort;
                    _activeRemoteKey = remoteKey;

                    if (prevStart.HasValue)
                    {
                        _instanceStartUtc = prevStart.Value;
                        DebugLog("STORE", "concurrent-resume rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if (idleActive)
                    {
                        DebugLog("STORE", "concurrent-new rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if (_pendingDisconnectUntilUtc != DateTime.MinValue && nowUtc <= _pendingDisconnectUntilUtc)
                    {
                        _instanceStartUtc = ResolveStartUtc(nowUtc, remoteKey);
                    }
                    else if (_suppressRolloverResetUntilUtc != DateTime.MinValue
                        && nowUtc <= _suppressRolloverResetUntilUtc
                        && _instanceStartUtc != DateTime.MinValue)
                    {
                        DebugLog("GUARD", "skip-reset localPort-rollover rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if ((!ReconnectPulseActive() && !HoldInstanceTimerDuringIdle()) || _instanceStartUtc == DateTime.MinValue)
                    {
                        _instanceStartUtc = ResolveStartUtc(nowUtc, remoteKey);
                    }

                    _lastPacketUtc = nowUtc;
                    RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                    DebugLog("STORE", "steady rk=" + remoteKey + " state=" + SnapshotUnsafe());
                }
                else if (!string.Equals(_activeRemoteKey, remoteKey, StringComparison.Ordinal))
                {
                    _activeRemoteKey = remoteKey;

                    if (_pendingDisconnectUntilUtc != DateTime.MinValue && nowUtc <= _pendingDisconnectUntilUtc)
                        _instanceStartUtc = ResolveStartUtc(nowUtc, remoteKey);
                    else if (_suppressRolloverResetUntilUtc != DateTime.MinValue
                        && nowUtc <= _suppressRolloverResetUntilUtc
                        && _instanceStartUtc != DateTime.MinValue)
                    {
                        DebugLog("GUARD", "skip-reset remote-change rk=" + remoteKey + " state=" + SnapshotUnsafe());
                    }
                    else if ((!ReconnectPulseActive() && !HoldInstanceTimerDuringIdle()) || _instanceStartUtc == DateTime.MinValue)
                        _instanceStartUtc = ResolveStartUtc(nowUtc, remoteKey);

                    _lastPacketUtc = nowUtc;
                    RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                    DebugLog("STORE", "remote-change rk=" + remoteKey + " state=" + SnapshotUnsafe());
                }
                else
                {
                    if (_instanceStartUtc == DateTime.MinValue)
                        _instanceStartUtc = ResolveStartUtc(nowUtc, remoteKey);
                    _lastPacketUtc = nowUtc;
                    RememberConnectionStartUnsafe(localPort, _instanceStartUtc, nowUtc);
                }
            }

            DateTime ResolveStartUtc(DateTime fallbackNow, string rk)
            {
                if (_pendingDisconnectUntilUtc == DateTime.MinValue
                    || fallbackNow > _pendingDisconnectUntilUtc
                    || _instanceStartBeforeDisconnectUtc == DateTime.MinValue)
                {
                    return fallbackNow;
                }

                string? curFp  = Get3074SessionFingerprint();
                bool fp3074    = !string.IsNullOrWhiteSpace(_fingerprintBeforeDisconnect)
                              && !string.IsNullOrWhiteSpace(curFp)
                              && string.Equals(_fingerprintBeforeDisconnect, curFp, StringComparison.Ordinal);
                string? curIp  = ExtractIpFromRemoteKey(rk);
                bool sameIp    = !string.IsNullOrWhiteSpace(_remoteIpBeforeDisconnect)
                              && !string.IsNullOrWhiteSpace(curIp)
                              && string.Equals(_remoteIpBeforeDisconnect, curIp, StringComparison.Ordinal);
                bool doPreserve = _preserveStartAcrossReconnect
                               || (_preserveSessionUntilUtc != DateTime.MinValue && fallbackNow <= _preserveSessionUntilUtc);

                _pendingDisconnectUntilUtc    = DateTime.MinValue;
                _fingerprintBeforeDisconnect  = null;
                _remoteIpBeforeDisconnect     = null;
                _preserveStartAcrossReconnect = false;

                if (doPreserve && (fp3074 || sameIp))
                {
                    _preserveSessionUntilUtc    = fallbackNow.Add(RejoinDecisionWindow);
                    _suppressRolloverResetUntilUtc = fallbackNow.Add(PreserveRolloverGuard);
                    DebugLog("RESOLVE", $"preserve-hit same3074={fp3074} sameIp={sameIp} rk={rk} state={SnapshotUnsafe()}");
                    var saved = _instanceStartBeforeDisconnectUtc;
                    _instanceStartBeforeDisconnectUtc = DateTime.MinValue;
                    return saved;
                }

                DebugLog("RESOLVE", $"fallback same3074={fp3074} sameIp={sameIp} rk={rk} state={SnapshotUnsafe()}");
                _instanceStartBeforeDisconnectUtc = DateTime.MinValue;
                return fallbackNow;
            }
        }

        public override bool AllowPacket(Packet p)
        {
            Interlocked.Increment(ref _packetCount);

            var text = Encoding.ASCII.GetString(p.Payload);
            var matchesAny = Regex.Matches(text, @"(?!\d)[A-Z|a-z|\d]{7,}");
            if (matchesAny.Any())
            {
                string message = matchesAny[0].Value.StartsWith("DESTINY")
                    ? $"{Name}: New instance"
                    : $"{Name}: {(p.Inbound ? "DL" : "UL")} {p.Length} - {string.Join(" > ", matchesAny.Select(x => x.Value))}";
                Logger.Debug(message);
            }

            return base.AllowPacket(p);
        }

        // ── Public API used by other modules / overlay ──────────────────────────

        public TimeSpan InstanceDuration()
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime? start;
            lock (_timerLock)
            {
                start = ResolvePrimaryCandidateStartUnsafe(nowUtc);
                var oldest = GetOldestAliveConnectionStartUnsafe(nowUtc);
                if (oldest.HasValue && (!start.HasValue || oldest.Value < start.Value))
                    start = oldest;
            }
            if (!start.HasValue) return TimeSpan.Zero;
            var dur = nowUtc - start.Value;
            return dur < TimeSpan.Zero ? TimeSpan.Zero : dur;
        }

        public bool IsInstanceActive()
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (_timerLock) { return ResolvePrimaryCandidateStartUnsafe(nowUtc).HasValue; }
        }

        public DateTime? GetInstanceStartUtc()
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (_timerLock) { return ResolvePrimaryCandidateStartUnsafe(nowUtc); }
        }

        public string? GetActiveRemoteKey()
        {
            lock (_timerLock) { return _activeRemoteKey; }
        }

        public bool HasReconnectPreservePending()
        {
            lock (_timerLock)
            {
                return _instanceStartBeforeDisconnectUtc != DateTime.MinValue
                    && _pendingDisconnectUntilUtc != DateTime.MinValue
                    && DateTime.UtcNow <= _pendingDisconnectUntilUtc;
            }
        }

        public void ArmPreserveAcrossReconnectFromHoldRelease()
        {
            lock (_timerLock)
            {
                var start = _instanceStartUtc != DateTime.MinValue
                    ? _instanceStartUtc : _instanceStartBeforeDisconnectUtc;
                if (start == DateTime.MinValue) return;

                DateTime now = DateTime.UtcNow;
                _pendingDisconnectUntilUtc        = now.Add(RejoinDecisionWindow);
                _instanceStartBeforeDisconnectUtc = start;
                _instanceStartUtc                 = start;
                _fingerprintBeforeDisconnect       = Get3074SessionFingerprint();
                _remoteIpBeforeDisconnect          = ExtractIpFromRemoteKey(_activeRemoteKey);
                _preserveStartAcrossReconnect      = true;
                _preserveSessionUntilUtc           = now.Add(RejoinDecisionWindow);
                DebugLog("ARM", "ArmPreserveAcrossReconnectFromHoldRelease post=" + SnapshotUnsafe());
            }
        }

        public bool TryConsumeRecentSynStart(string remoteKey, TimeSpan maxAge)
        {
            if (string.IsNullOrWhiteSpace(remoteKey)) return false;
            lock (_timerLock)
            {
                if (string.IsNullOrWhiteSpace(_lastSynStartRemoteKey)) return false;
                if (!string.Equals(_lastSynStartRemoteKey, remoteKey, StringComparison.Ordinal)) return false;
                if (_lastSynStartUtc == DateTime.MinValue) return false;
                if (DateTime.UtcNow - _lastSynStartUtc > maxAge) return false;
                _lastSynStartRemoteKey = null;
                _lastSynStartUtc       = DateTime.MinValue;
                return true;
            }
        }

        public void ResetInstanceTimer()
        {
            lock (_timerLock)
            {
                DebugLog("RESET", "ResetInstanceTimer pre=" + SnapshotUnsafe());
                _activeLocalPort                  = null;
                _activeRemoteKey                  = null;
                _instanceStartUtc                 = DateTime.MinValue;
                _lastPacketUtc                    = DateTime.MinValue;
                _pendingDisconnectUntilUtc         = DateTime.MinValue;
                _instanceStartBeforeDisconnectUtc  = DateTime.MinValue;
                _fingerprintBeforeDisconnect        = null;
                _remoteIpBeforeDisconnect           = null;
                _suppressRolloverResetUntilUtc      = DateTime.MinValue;
                _preserveSessionUntilUtc            = DateTime.MinValue;
                _preserveStartAcrossReconnect       = false;
                _connectionStarts.Clear();
            }
        }

        // ── Private helpers ─────────────────────────────────────────────────────

        private DateTime? ResolvePrimaryCandidateStartUnsafe(DateTime nowUtc)
        {
            if (_lastPacketUtc != DateTime.MinValue)
            {
                bool pending = _pendingDisconnectUntilUtc != DateTime.MinValue
                            && nowUtc <= _pendingDisconnectUntilUtc
                            && _instanceStartBeforeDisconnectUtc != DateTime.MinValue;

                if (nowUtc - _lastPacketUtc <= IdleGap || HoldInstanceTimerDuringIdle() || pending)
                {
                    if (_instanceStartUtc != DateTime.MinValue) return _instanceStartUtc;
                    if (pending) return _instanceStartBeforeDisconnectUtc;
                }
                return null;
            }

            if (_pendingDisconnectUntilUtc != DateTime.MinValue
                && nowUtc <= _pendingDisconnectUntilUtc
                && _instanceStartBeforeDisconnectUtc != DateTime.MinValue)
            {
                return _instanceStartBeforeDisconnectUtc;
            }
            return null;
        }

        private DateTime? GetOldestAliveConnectionStartUnsafe(DateTime nowUtc)
        {
            DateTime? result = null;
            foreach (var v in _connectionStarts.Values)
            {
                if (nowUtc - v.LastSeen > IdleGap) continue;
                if (!result.HasValue || v.Start < result.Value) result = v.Start;
            }
            return result;
        }
    }
}
