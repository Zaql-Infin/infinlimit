using InfinLimit.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

using WindivertDotnet;

namespace InfinLimit.Interception.PacketProviders
{
    public class PlayersProvider : PacketProviderBase
    {
        // tickrate 30
        public PlayersProvider() : base ("Players", 27015, 27200)
        {
        }

        public class ResyncInfo
        {
            public DateTime LastClosed { get; set; }
            public DateTime LastStart { get; set; }
            public DateTime LastEndInbound { get; set; }
            public DateTime LastEndOutbound { get; set; }

            public List<string> Ids { get; set; } = new ();

            public bool InboundActive => LastEndInbound < LastStart;
            public bool OutboundActive => LastEndOutbound < LastStart;
            public bool Active => InboundActive && OutboundActive;
        }

        public Dictionary<string, ResyncInfo> Resyncs { get; set; } = new();
        public override bool AllowPacket(Packet p)
        {
            //if (!Resyncs.TryGetValue(p.RemoteAddress, out var resync))
            //    resync = Resyncs[p.RemoteAddress] = new ResyncInfo();

            //void TryAddId(string id)
            //{
            //    if (!resync.Ids.Contains(id))
            //    {
            //        resync.Ids.Add(id);
            //        Logger.Debug($"{Name}: {p.RemoteAddress} Synced with {id}");
            //    }
            //}

            //var text = Encoding.ASCII.GetString(p.Payload);
            //bool id = true;
            //var matches = Regex.Matches(text, @"((?:xbox|psn|xbox|egs)-.{16}|steamid:\d{17})");
            //if (!matches.Any())
            //{
            //    id = false;
            //    matches = Regex.Matches(text, @"(?!\d)[A-Z|a-z|\d]{6,}");
            //}

            //// 315 out (2 ids) - we send we start talking
            //// 1300 out no matches is start talking on prev established,    expects 1300 dl with gibber >50sum
            //// ping gives new ip to talk to,                                expects 1300 dl with gibber >50sum
            //// Application out -> low id in -> higher id in (talk again?) -> 1300 out ping
            //// low steamid len after closed from local is acknowledgement

            //if (id)
            //{
            //    if (matches.Count == 1)
            //    {
            //        var id1 = p.Outbound ? matches[0].Value : matches[0].Value;
            //        TryAddId(id1);
            //        if (DateTime.Now - resync.LastClosed < TimeSpan.FromSeconds(5))
            //        {
            //            Logger.Debug($"{Name}: DL:{p.Inbound} {p.RemoteAddress}: Close acknowledge");
            //            return true;
            //        }
            //        Logger.Debug($"{Name}: DL:{p.Inbound} {p.RemoteAddress}: Just some id");
            //    }
            //    else if (matches.Count == 2)
            //    {
            //        var id2 = p.Outbound ? matches[1].Value : matches[1].Value;
            //        TryAddId(id2);
            //    }
            //}

            //if (matches.Any())
            //{
            //    var first = matches[0].Value;
            //    // pre resync stuff
            //    if (first.StartsWith("Application"))
            //    {
            //        resync.LastStart = p.CreatedAt;
            //        ClearDelayQueue(p.RemoteAddress);
            //        Logger.Debug($"{Name}: {p.RemoteAddress} {(p.Inbound ? "Remote closed the connection" : "We closed the connection [START]")}");
            //    }
            //    else // ping starts resync
            //    if (first.Contains("ping", StringComparison.OrdinalIgnoreCase))
            //    {
            //        resync.LastStart = p.CreatedAt;
            //        Logger.Debug($"{Name}: {p.RemoteAddress} DL:{p.Inbound} Start ping");
            //    }
            //}

            //// no match 1300 starts resync (connection was kept silent)
            //if (p.Outbound && p.Length == 1300 && !resync.Active)
            //{
            //    resync.LastStart = p.CreatedAt;
            //    Logger.Debug($"{Name}: {p.RemoteAddress} DL:{p.Inbound} Start from old connection");
            //    return true;
            //}


            //// RESYNC
            //if (p.Inbound && resync.InboundActive)
            //{
            //    if (p.Length > 1000 && p.Length < 1300)
            //    {
            //        resync.LastEndInbound = p.CreatedAt;
            //        Logger.Debug($"{Name}: {p.RemoteAddress} DL Finished");
            //    }

            //    return true;
            //}
            //if (p.Outbound && resync.OutboundActive)
            //{
            //    if (p.Length > 1000 && p.Length < 1300)
            //    {
            //        resync.LastEndOutbound = p.CreatedAt;
            //        Logger.Debug($"{Name}: {p.RemoteAddress} UL Finished");
            //    }

            //    return true;
            //}
            //if (resync.Active && p.CreatedAt - resync.LastStart > TimeSpan.FromSeconds(10))
            //{
            //    if (resync.OutboundActive) resync.LastEndOutbound = p.CreatedAt;
            //    if (resync.InboundActive) resync.LastEndInbound = p.CreatedAt;
            //    Logger.Debug($"{Name}: {p.RemoteAddress} Finished with fallback [10s]");
            //}

            //var sum = string.Join(">", matches.Select(x => x.Value));
            return base.AllowPacket(p);
        }




        protected override WinDivert CreateInstance()
        {
            return Divert = new WinDivert(Filter.True.And(x => x.IsUdp && x.Network.RemotePort >= 27015 && x.Network.RemotePort <= 27200), WinDivertLayer.Network);
        }
    }
}
