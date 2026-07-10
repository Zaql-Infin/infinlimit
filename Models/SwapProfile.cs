using System.Collections.Generic;
using InfinLimit.Utility;

namespace InfinLimit.Models
{
    public class SwapProfile
    {
        public string Name { get; set; } = "Profile";
        public List<Keycode> Keybind { get; set; } = new();
        public int SwapDuration { get; set; } = 2000;
        public int SwapDelay { get; set; } = 25;
        public int UntickDelay { get; set; } = 500;
        public int EndingLoadout { get; set; } = 1;
        public bool Port3074 { get; set; } = true;
        public bool Port27k { get; set; }
        public bool Packet3074DL { get; set; }
        public bool AutoDisableBuffering { get; set; }
        public bool CloseInventory { get; set; }
        public bool FullGame { get; set; }
        public bool OpenInventory { get; set; }
        public bool[] LoadoutEnabled { get; set; } = new bool[20];
    }
}
