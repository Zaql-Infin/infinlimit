using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InfinLimit.Utility
{
    public static class InputSimulator
    {
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(Keys vKey);

        public static void MouseMove(int x, int y) => SetCursorPos(x, y);

        public static void MouseClick()
        {
            mouse_event(0x02 | 0x04, 0, 0, 0, UIntPtr.Zero); // Left down | up
        }

        public static void KeyPress(Keys key)
        {
            SendKeys.SendWait("{" + key.ToString() + "}");
        }

        public static void Sleep(int ms)
        {
            Thread.Sleep(ms);
        }
    }
}