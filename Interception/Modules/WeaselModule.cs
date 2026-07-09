using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

using InfinLimit.Models;

namespace InfinLimit.Interception.Modules
{
    public class WeaselModule : PacketModuleBase
    {
        private PacketProviderBase _7500;

        public WeaselModule()
            : base("Weasel", false, InterceptionManager.GetProvider("7500"))
        {
            IsActivated = false;
            Icon = System.Windows.Application.Current.TryFindResource("WeaselIcon") as Geometry ?? Icon;
            Description = "RST inject on active 7500 connections\nForce-disconnects the API session";
            _7500 = PacketProviders.First();
        }

        public override void Toggle()
        {
            IsActivated = true;
            StartTime = DateTime.Now;
            string[] keys = _7500.Connections.Keys.ToArray();
            foreach (string addr in keys)
            {
                try
                {
                    if (_7500.Connections.TryGetValue(addr, out List<Packet> value) && value != null)
                    {
                        DateTime? lastSeen = value.LastOrDefault()?.CreatedAt;
                        if (DateTime.Now - lastSeen < TimeSpan.FromSeconds(10))
                            Inject(addr);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }
            Task.Run(async () =>
            {
                await Task.Delay(150);
                IsActivated = false;
            });
        }

        private unsafe void Inject(string addr)
        {
            List<Packet> packets = _7500.Connections[addr];
            Packet outPacket = packets.LastOrDefault(x => !x.Inbound && x.Length != 0);
            Packet inPacket = packets.LastOrDefault(x => x.Inbound && x.Length != 0);
            if (outPacket == null || inPacket == null) return;

            Packet p1 = outPacket.BuildSameDirection();
            p1.ParseResult.TcpHeader->Rst = true;
            p1.Recalc();
            _7500.StorePacket(p1);
            _7500.SendPacket(p1, force: true);

            Packet p2 = inPacket.BuildSameDirection();
            p2.ParseResult.TcpHeader->Rst = true;
            p2.Recalc();
            _7500.StorePacket(p2);
            _7500.SendPacket(p2, force: true);
        }
    }
}
