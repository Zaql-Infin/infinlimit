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
using System.Windows.Forms;
using System.Windows.Media;

using WindivertDotnet;

namespace InfinLimit.Interception.Modules
{
    public class ReconnectModule : PacketModuleBase
    {
        PacketProviderBase _30k;
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
        }

        public override void Toggle()
        {
            IsActivated = true;
            StartTime = DateTime.Now;

            foreach (var addr in _30k.Connections.Keys.ToArray())
            {
                try
                {
                    if (_30k.Connections.TryGetValue(addr, out var q) && q is not null && DateTime.Now - q.LastOrDefault()?.CreatedAt < TimeSpan.FromSeconds(10))
                        Inject(addr);

                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
                
            }

            Task.Run(async () =>
            {
                await Task.Delay(150);
                IsActivated = false;
            });
        }

        // TODO: Try find ammo saving method, look at how landing zone does it
        unsafe void Inject(string addr)
        {
            var con = _30k.Connections[addr];
            var out_example = con.LastOrDefault(x => !x.Inbound && x.Length != 0);
            var in_example = con.LastOrDefault(x => x.Inbound && x.Length != 0);

            // TODO: just build a packet lol
            if (out_example is null || in_example is null)
            {
                Logger.Warning($"{Name}: Can't kill {addr}");
                return;
            }

            #region fin

            ////inbound fin // solo sec // fireteam 8-15 sec // fin o - until server fin i
            //var p2 = in_example.BuildSameDirection();
            //p2.ParseResult.TcpHeader->Fin = true;
            //p2.ParseResult.TcpHeader->Ack = true;
            //p2.ParseResult.TcpHeader->AckNum = TcpReordering.HighAck[p2.RemoteAddress][FlagType.Remote];
            //p2.ParseResult.TcpHeader->SeqNum = TcpReordering.HighSeq[p2.RemoteAddress][FlagType.Remote] + TcpReordering.LastLength[p2.RemoteAddress][FlagType.Remote];

            //_30k.StorePacket(p2);
            //_30k.SendPacket(p2, true);


            //// outbound fin // solo sec // fireteam 8 sec // fin i - fin o - rst i - syn o 
            //var p1 = out_example.BuildSameDirection();
            //p1.ParseResult.TcpHeader->Fin = true;
            //p1.ParseResult.TcpHeader->Ack = true;
            //p1.ParseResult.TcpHeader->AckNum = TcpReordering.HighAck[p1.RemoteAddress][FlagType.Local];
            //p1.ParseResult.TcpHeader->SeqNum = TcpReordering.HighSeq[p1.RemoteAddress][FlagType.Local] + TcpReordering.LastLength[p1.RemoteAddress][FlagType.Local];
            //_30k.StorePacket(p1);
            //_30k.SendPacket(p1, true);

            #endregion

            #region rst
            // both // solo // fireteam 8 sec
            // outbound rst // solo sec // fireteam 10 sec
            var p1 = out_example.BuildSameDirection();
            p1.ParseResult.TcpHeader->Rst = true;
            p1.Recalc();
            _30k.StorePacket(p1);
            _30k.SendPacket(p1, true);

            // inbound rst // solo 6 sec // fireteam 40 sec
            var p2 = in_example.BuildSameDirection();
            p2.ParseResult.TcpHeader->Rst = true;
            p2.Recalc();
            _30k.StorePacket(p2);
            _30k.SendPacket(p2, true);
            #endregion

            #region syn

            //// inbound syn // solo 6 sec // fireteam ~1min
            //// causes out rst
            //var p2 = in_example.BuildSameDirection();
            //p2.ParseResult.TcpHeader->Syn = true;
            //p2.ParseResult.TcpHeader->Ack = false;
            ////p2.ParseResult.TcpHeader->AckNum = TcpReordering.HighAck[p2.RemoteAddress][FlagType.Remote];
            //p2.ParseResult.TcpHeader->SeqNum = 63969235;
            //
            //_30k.StorePacket(p2);
            //_30k.SendPacket(p2, true);

            //// outbound syn / fin - rst o!push
            //// causes inb rst
            //var p1 = out_example.BuildSameDirection();
            //p1.ParseResult.TcpHeader->Syn = true;
            //p1.ParseResult.TcpHeader->Ack = false;
            ////p1.ParseResult.TcpHeader->AckNum = TcpReordering.HighAck[p1.RemoteAddress][FlagType.Local];
            //p1.ParseResult.TcpHeader->SeqNum = 63969235;
            //_30k.StorePacket(p1);
            //_30k.SendPacket(p1, true);
            #endregion
        }
    }
}
