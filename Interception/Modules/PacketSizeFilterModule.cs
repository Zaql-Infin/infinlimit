using System;
using System.Windows.Media;

using InfinLimit.Models;

namespace InfinLimit.Interception.Modules
{
    public class PacketSizeFilterModule : PacketModuleBase
    {
        private readonly Random _rng = new Random();
        private readonly PacketProviderBase _provider;

        public int MinSize;
        public int MaxSize = int.MaxValue;
        public bool Invert;
        public int RandomMinMs = 200;
        public int RandomMaxMs = 800;

        public PacketSizeFilterModule(string displayName, PacketProviderBase provider)
            : base(" SizeFilter " + displayName + " ", true, provider)
        {
            _provider = provider;
            Icon = System.Windows.Application.Current.FindResource("DefaultIcon") as Geometry;
            Description = "Size-range filter on " + displayName + " with invert mode and a randomized buffer window.";
            try
            {
                var named = Config.GetNamed(Name);
                MinSize = named.GetSettings<int>("MinSize");
                int max = named.GetSettings<int>("MaxSize");
                MaxSize = max > 0 ? max : int.MaxValue;
                Invert = named.GetSettings<bool>("Invert");
                int rMin = named.GetSettings<int>("RandomMinMs");
                int rMax = named.GetSettings<int>("RandomMaxMs");
                if (rMin > 0) RandomMinMs = rMin;
                if (rMax > 0) RandomMaxMs = rMax;
            }
            catch { }
        }

        public void SaveSettings()
        {
            try
            {
                var named = Config.GetNamed(Name);
                named.Settings["MinSize"] = MinSize;
                named.Settings["MaxSize"] = MaxSize != int.MaxValue ? MaxSize : 0;
                named.Settings["Invert"] = Invert;
                named.Settings["RandomMinMs"] = RandomMinMs;
                named.Settings["RandomMaxMs"] = RandomMaxMs;
                Config.Save();
            }
            catch { }
        }

        public override void Toggle() => IsActivated = !IsActivated;

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;
            if (!IsActivated || p.Length == 0) return true;

            bool inRange = p.Length >= MinSize && p.Length <= MaxSize;
            if (!(Invert ? !inRange : inRange)) return true;

            int lo = Math.Min(RandomMinMs, RandomMaxMs);
            int hi = Math.Max(RandomMinMs, RandomMaxMs);
            int ms = lo == hi ? lo : _rng.Next(lo, hi + 1);
            try { _provider.DelayPacket(p, TimeSpan.FromMilliseconds(ms), addFromLatest: true, sameDirection: true); }
            catch { }
            return false;
        }
    }
}
