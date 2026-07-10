using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace InfinLimit.Utility
{
    public static class CaptureGuard
    {
        private const uint WDA_NONE             = 0;
        private const uint WDA_EXCLUDEFROMCAPTURE = 17;

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        public static void Apply(Window w)
        {
            var handle = new WindowInteropHelper(w).Handle;
            if (handle == IntPtr.Zero) return;
            uint affinity = Config.Instance.Settings.Overlay_HideFromCapture
                ? WDA_EXCLUDEFROMCAPTURE
                : WDA_NONE;
            try { SetWindowDisplayAffinity(handle, affinity); } catch { }
        }

        public static void ApplyToAll()
        {
            foreach (Window w in Application.Current.Windows)
                Apply(w);
        }
    }
}
