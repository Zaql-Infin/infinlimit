using InfinLimit.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace InfinLimit.Interception.Modules
{
    public class TestModule : PacketModuleBase
    {
        PacketProviderBase players; // InterceptionManager.GetProvider("Players"),
        PacketProviderBase api;
        public TestModule() : base("Test", true, InterceptionManager.GetProvider("7500"))
        {
            players = InterceptionManager.GetProvider("Players");
            api = InterceptionManager.GetProvider("7500");
        }


        // TODO: Test 7500 reorder
        public override void Toggle()
        {
            IsActivated = !IsActivated;
        }

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;

            if (!IsActivated) return true;

            if (p.SourceProvider == players) return true;

            return false;
        }








        //public override void Toggle()
        //{
        //    IsActivated = !IsActivated;
        //    if (!IsActivated)
        //    {
        //        for (int i = 0; i < hold.Count; i++)
        //        {
        //            api.SendPacket(hold[i]);
        //            hold[i].Delayed = false;
        //        }
        //        hold.Clear();
        //    }
        //}

        //List<Packet> hold = new List<Packet>();
        //uint latestSeq = 0;
        //uint latestAck = 0;
        //public override bool AllowPacket(Packet p)
        //{
        //    if (!base.AllowPacket(p)) return false;

        //    if (p.Outbound && p.AckNum.HasValue && p.AckNum < latestSeq)
        //    {
        //        unsafe
        //        {
        //            p.ParseResult.TcpHeader->AckNum = latestSeq;
        //            p.Recalc();
        //        }
        //    }

        //    if (p.Inbound && p.SeqNum <= latestSeq)
        //    {
        //        // if push respond with ack?
        //        return false;
        //    }

        //    if (!IsActivated) return true;

        //    if (p.SourceProvider == players) return true;

        //    if (p.Outbound) return true;

        //    if (!hold.Any() && p.Length == 0) return true;

        //    if (hold.Any() && p.SeqNum.HasValue && p.SeqNum + p.Length == hold.Max(x => x.SeqNum + x.Length))
        //        return false;

        //    if (p.Length == 0) return false;

        //    p.Delayed = true;
        //    hold.Add(p);

        //    if (hold.Count(x => x.Length == 41) == 2)
        //    {
        //        // disable sequence
        //        var start = hold.Min(x => x.SeqNum);
        //        var sent = 0u;
        //        var acks = hold.Select(x => x.AckNum.Value).OrderBy(x => x).ToArray();
        //        latestSeq = hold.Max(x => x.SeqNum.Value + (uint)x.Length);
        //        latestAck = acks.Last();
        //        var ackIndex = 0;
        //        for (int i = hold.Count - 2; i > 0; i--)
        //        {
        //            var el = hold[i];
        //            if (el.Length == 41)
        //            {
        //                for (int j = i + 1; j < hold.Count; j++)
        //                {
        //                    var send = hold[j];
        //                    unsafe
        //                    {
        //                        send.ParseResult.TcpHeader->AckNum = acks[ackIndex++];
        //                        send.ParseResult.TcpHeader->SeqNum = start.Value + sent;
        //                        sent += (uint)send.Length;
        //                    }
        //                    send.Recalc();
        //                    api.SendPacket(send).ContinueWith(x => { send.Delayed = false; });
        //                }

        //                for (int z = 0; z <= i; z++)
        //                {
        //                    var send = hold[z];
        //                    unsafe
        //                    {
        //                        send.ParseResult.TcpHeader->AckNum = acks[ackIndex++];
        //                        send.ParseResult.TcpHeader->SeqNum = start.Value + sent;
        //                        sent += (uint)send.Length;
        //                    }
        //                    send.Recalc();
        //                    api.SendPacket(send).ContinueWith(x => { send.Delayed = false; });
        //                }

        //                hold.Clear();
        //                ForceDisable();
        //                // kicks with weasel, so there are some integrity checks, can only delay
        //            }
        //        }
        //    }

        //    return false;
        //}
    }
}
