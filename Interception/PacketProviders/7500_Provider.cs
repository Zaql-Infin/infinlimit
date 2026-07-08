using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WindivertDotnet;

namespace InfinLimit.Interception.PacketProviders
{
    public class _7500_Provider : PacketProviderBase
    {
        // USE CASES:
        // Collect flag

        // Involves:
        // Buying from vendors

        // Known patterns:
        //  packets usually start with 0101000.. but first 4 packets are starting with 0102000.. (might be key exchange)
        //
        //  28/37 like a ping packet (each 5 secs), usual response is 30
        //  96 both out and in

        // - Outbound:
        //  1452 seems to be max packet length
        //  1452 x5 stacks ending with usually 327 (each 30 secs)
        //  543 ?
        //  256 ?

        // - Inbound:
        //  double packet starting with 536 - get engram loot

        // Error codes: Weasel (out ?)
        public _7500_Provider() : base("7500", 7500, 7509, true)
        {
        }

        protected override WinDivert CreateInstance()
        {
            return Divert = new WinDivert(Filter.True.And(x => x.IsTcp && x.Network.RemotePort >= 7500 && x.Network.RemotePort <= 7509), WinDivertLayer.Network);
        }
    }
}
