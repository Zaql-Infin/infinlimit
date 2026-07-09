using Microsoft.EntityFrameworkCore;

using InfinLimit.Database;
using InfinLimit.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;

using WindivertDotnet;

namespace InfinLimit.Interception.Modules
{
    public class ReconnectModule : PacketModuleBase
    {
        public enum InjectMode { Rst, Fin, Syn }

        private readonly PacketProviderBase _30k;

        private static DateTime _holdUntilUtc = DateTime.MinValue;
        private static bool _holdWaitingForTraffic;

        public static DateTime LastReconnectUtc { get; private set; } = DateTime.MinValue;
        public static long TriggerCount { get; private set; } = 0;
        public static double HoldSeconds { get; private set; } = 0;
        public static InjectMode Mode { get; private set; } = InjectMode.Rst;

        public ReconnectModule() : base("Reconnect", false, InterceptionManager.GetProvider("30000"))
        {
            IsActivated = false;
            Icon = System.Windows.Application.Current.FindResource("ReconnectIcon") as Geometry ?? Icon;
            Description =
@"Instant reconnect
Change public instances
Reload world state
Pull yourself to team leader";

            _30k = PacketProviders.First();
            try
            {
                HoldSeconds = Math.Clamp(Math.Round(Config.GetNamed(Name).GetSettings<double>("HoldSeconds")), 0, 10);
            }
            catch { HoldSeconds = 0; }
            Mode = InjectMode.Rst;
        }

        public static void SetHoldSeconds(double seconds)
        {
            HoldSeconds = Math.Clamp(Math.Round(seconds), 0, 10);
        }

        public static void SetInjectMode(InjectMode mode) => Mode = mode;

        private static void BeginHoldWindow()
        {
            _holdWaitingForTraffic = true;
            _holdUntilUtc = DateTime.MinValue;
        }

        private static bool IsHoldWindowActive()
        {
            if (_holdWaitingForTraffic) return true;
            return DateTime.UtcNow < _holdUntilUtc;
        }

        public override void Toggle()
        {
            IsActivated = true;
            StartTime = DateTime.Now;
            LastReconnectUtc = DateTime.UtcNow;
            TriggerCount++;

            // Tell Activity_30k not to auto-arm for 10 seconds after a reconnect
            Activity_30k.DoNotArmForSeconds(10);
            Activity_30k.BeginReconnectBaselineCapture(10);
            if (InterceptionManager.GetModule("Activity") is Activity_30k activity)
                activity.ForceStop();

            foreach (var addr in _30k.Connections.Keys.ToArray())
            {
                try
                {
                    if (_30k.Connections.TryGetValue(addr, out var q) && q is not null && q.Count != 0)
                    {
                        var last = q.LastOrDefault();
                        if (last != null && DateTime.Now - last.CreatedAt < TimeSpan.FromSeconds(10))
                            Inject(addr);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            BeginHoldWindow();
            AutoOffAsync();
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;
            if (!IsHoldWindowActive()) return true;

            // On first real inbound traffic, start the countdown
            if (_holdWaitingForTraffic && !p.Delayed && p.Length > 73)
            {
                _holdWaitingForTraffic = false;
                _holdUntilUtc = HoldSeconds > 0
                    ? DateTime.UtcNow.AddSeconds(HoldSeconds)
                    : DateTime.MinValue;
            }

            if (p.Delayed || p.Outbound || p.Length == 0 || p.Length <= 73) return true;

            TimeSpan remaining = _holdUntilUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return true;

            try
            {
                p.SourceProvider.DelayPacket(p, remaining, addFromLatest: true, sameDirection: true);
                return false;
            }
            catch { return true; }
        }

        private async Task AutoOffAsync()
        {
            try { await Task.Delay(250); } catch { }
            IsActivated = false;
        }

        void Inject(string addr)
        {
            var con = _30k.Connections[addr];
            var outPacket = con.LastOrDefault(x => !x.Inbound && x.Length != 0);
            var inPacket = con.LastOrDefault(x => x.Inbound && x.Length != 0);

            if (outPacket is null || inPacket is null)
            {
                Logger.Warning($"{Name}: Can't kill {addr}");
                return;
            }

            switch (Mode)
            {
                case InjectMode.Fin: InjectFin(outPacket, inPacket); break;
                case InjectMode.Syn: InjectSyn(outPacket, inPacket); break;
                default: InjectRst(outPacket, inPacket); break;
            }
        }

        unsafe void InjectRst(Packet outExample, Packet inExample)
        {
            var p1 = outExample.BuildSameDirection();
            p1.ParseResult.TcpHeader->Rst = true;
            p1.ParseResult.TcpHeader->Fin = false;
            p1.ParseResult.TcpHeader->Syn = false;
            p1.Recalc();
            _30k.StorePacket(p1);
            _30k.SendPacket(p1, force: true);

            var p2 = inExample.BuildSameDirection();
            p2.ParseResult.TcpHeader->Rst = true;
            p2.ParseResult.TcpHeader->Fin = false;
            p2.ParseResult.TcpHeader->Syn = false;
            p2.Recalc();
            _30k.StorePacket(p2);
            _30k.SendPacket(p2, force: true);
        }

        unsafe void InjectFin(Packet outExample, Packet inExample)
        {
            bool hasCache = TcpReordering.Cache.TryGetValue(outExample.RemoteAddress, out TcpCache cache)
                && cache?.Location.ContainsKey(FlagType.Local) == true
                && cache.Location.ContainsKey(FlagType.Remote);

            var p1 = outExample.BuildSameDirection();
            p1.ParseResult.TcpHeader->Fin = true;
            p1.ParseResult.TcpHeader->Ack = true;
            p1.ParseResult.TcpHeader->Rst = false;
            p1.ParseResult.TcpHeader->Syn = false;
            if (hasCache)
            {
                var d = cache.Location[FlagType.Local];
                p1.ParseResult.TcpHeader->AckNum = d.HighAck;
                p1.ParseResult.TcpHeader->SeqNum = d.HighSeq + d.LastLength;
            }
            p1.Recalc();
            _30k.StorePacket(p1);
            _30k.SendPacket(p1, force: true);

            var p2 = inExample.BuildSameDirection();
            p2.ParseResult.TcpHeader->Fin = true;
            p2.ParseResult.TcpHeader->Ack = true;
            p2.ParseResult.TcpHeader->Rst = false;
            p2.ParseResult.TcpHeader->Syn = false;
            if (hasCache)
            {
                var d = cache.Location[FlagType.Remote];
                p2.ParseResult.TcpHeader->AckNum = d.HighAck;
                p2.ParseResult.TcpHeader->SeqNum = d.HighSeq + d.LastLength;
            }
            p2.Recalc();
            _30k.StorePacket(p2);
            _30k.SendPacket(p2, force: true);
        }

        unsafe void InjectSyn(Packet outExample, Packet inExample)
        {
            bool hasCache = TcpReordering.Cache.TryGetValue(outExample.RemoteAddress, out TcpCache cache)
                && cache?.Location.ContainsKey(FlagType.Local) == true
                && cache.Location.ContainsKey(FlagType.Remote);

            var p1 = outExample.BuildSameDirection();
            p1.ParseResult.TcpHeader->Syn = true;
            p1.ParseResult.TcpHeader->Ack = false;
            p1.ParseResult.TcpHeader->Rst = false;
            p1.ParseResult.TcpHeader->Fin = false;
            p1.ParseResult.TcpHeader->AckNum = 0;
            if (hasCache)
            {
                var d = cache.Location[FlagType.Local];
                p1.ParseResult.TcpHeader->SeqNum = d.HighSeq + d.LastLength;
            }
            p1.Recalc();
            _30k.StorePacket(p1);
            _30k.SendPacket(p1, force: true);

            var p2 = inExample.BuildSameDirection();
            p2.ParseResult.TcpHeader->Syn = true;
            p2.ParseResult.TcpHeader->Ack = false;
            p2.ParseResult.TcpHeader->Rst = false;
            p2.ParseResult.TcpHeader->Fin = false;
            p2.ParseResult.TcpHeader->AckNum = 0;
            if (hasCache)
            {
                var d = cache.Location[FlagType.Remote];
                p2.ParseResult.TcpHeader->SeqNum = d.HighSeq + d.LastLength;
            }
            p2.Recalc();
            _30k.StorePacket(p2);
            _30k.SendPacket(p2, force: true);
        }
    }
}
