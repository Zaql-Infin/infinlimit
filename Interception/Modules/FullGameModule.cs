using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Media;

using InfinLimit.Models;

namespace InfinLimit.Interception.Modules
{
    public class FullGameModule : PacketModuleBase
    {
        private double _tokens;
        private DateTime _lastTick = DateTime.Now;
        private readonly object _bucketLock = new object();
        private readonly ConcurrentQueue<Packet> _queue = new ConcurrentQueue<Packet>();
        private readonly Timer _timer;
        private volatile bool _processing;

        public static long TargetBytesPerSecond = 819200L;

        public FullGameModule()
            : base("Full Game", true,
                InterceptionManager.GetProvider("Xbox"),
                InterceptionManager.GetProvider("Players"),
                InterceptionManager.GetProvider("30000"),
                InterceptionManager.GetProvider("7500"))
        {
            Icon = System.Windows.Application.Current.FindResource("DefaultIcon") as Geometry;
            Description = "Token-bucket bandwidth cap across all game ports\nRate-limits Xbox + Players + 30k + 7500 together";
            long cfg = Config.GetNamed(Name).GetSettings<long>("TargetBytesPerSecond");
            if (cfg > 0) TargetBytesPerSecond = cfg;
            _timer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        }

        public override void Toggle()
        {
            IsActivated = !IsActivated;
            if (IsActivated)
            {
                lock (_bucketLock) { _tokens = 0; _lastTick = DateTime.Now; }
                return;
            }
            Packet p;
            while (_queue.TryDequeue(out p))
            {
                try
                {
                    p.CreatedAt = DateTime.Now;
                    p.Delayed = true;
                    p.AckNum = 0;
                    p.SourceProvider.StorePacket(p);
                    p.SourceProvider.DelayPacket(p, TimeSpan.FromMilliseconds(1));
                }
                catch { }
            }
            lock (_bucketLock) { _tokens = 0; _lastTick = DateTime.Now; }
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;
            if (!IsActivated) return true;
            if (p.Length == 0) return true;
            _queue.Enqueue(p);
            return false;
        }

        private void ProcessQueue(object state)
        {
            if (_processing || !IsActivated) return;
            _processing = true;
            try
            {
                DateTime now = DateTime.Now;
                lock (_bucketLock)
                {
                    double elapsed = (now - _lastTick).TotalSeconds;
                    _lastTick = now;
                    long cap = TargetBytesPerSecond > 0 ? TargetBytesPerSecond : 819200;
                    _tokens += cap * elapsed;
                    if (_tokens > cap) _tokens = cap;
                }
                Packet peek;
                while (_queue.TryPeek(out peek))
                {
                    lock (_bucketLock)
                    {
                        if (peek.Length > 0 && _tokens < peek.Length) break;
                        if (!_queue.TryDequeue(out Packet p)) break;
                        _tokens -= p.Length;
                        try
                        {
                            p.CreatedAt = DateTime.Now;
                            p.Delayed = true;
                            p.AckNum = 0;
                            p.SourceProvider.StorePacket(p);
                            p.SourceProvider.DelayPacket(p, TimeSpan.FromMilliseconds(1));
                        }
                        catch (Exception ex) { Logger.Error(ex, "FullGame token bucket release"); }
                    }
                }
            }
            catch (Exception ex) { Logger.Error(ex, "FullGame ProcessQueue"); }
            finally { _processing = false; }
        }

        public override void StopListening()
        {
            _timer?.Dispose();
            Packet p;
            while (_queue.TryDequeue(out p)) { }
            base.StopListening();
        }
    }
}
