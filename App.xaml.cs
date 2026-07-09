using Hardcodet.Wpf.TaskbarNotification.Interop;

using InfinLimit.Utility;

using System.Threading.Tasks;

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

using WpfSnowfall;

using Rectangle = System.Drawing.Rectangle;

namespace InfinLimit


{
    public partial class App : System.Windows.Application
    {
  
            protected override void OnStartup(StartupEventArgs e)
            {
                base.OnStartup(e);
            }

            private void Application_Startup(object sender, StartupEventArgs e)
            {
                EnsureNpcap();
                ThemeManager.Initialize();
                var checker = new IdentityChecker();
                checker.CheckSubs();
                var main = new MainWindow(checker);
                main.Show();

                // Background update check — runs after window opens so it doesn't delay startup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!await Updater.IsUpdateAvailableAsync()) return;

                        var result = Dispatcher.Invoke(() =>
                            System.Windows.MessageBox.Show(
                                "A new version of InfinLimit is available.\n\nInstall now and restart?",
                                "Update Available",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Information));

                        if (result == System.Windows.MessageBoxResult.Yes)
                            await Updater.DownloadAndApplyAsync();
                    }
                    catch { }
                });
            }

            private static void EnsureNpcap()
            {
                bool installed = System.IO.File.Exists(@"C:\Windows\System32\Npcap\wpcap.dll") ||
                    Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Npcap") != null;
                if (installed) return;

                var result = System.Windows.Forms.MessageBox.Show(
                    "Npcap is required for the Console Limiter (ARP spoofing).\nInstall it now?",
                    "Npcap Required",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Question);

                if (result != System.Windows.Forms.DialogResult.Yes) return;

                var installer = System.IO.Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory, "npcap-installer.exe");
                if (!System.IO.File.Exists(installer)) return;

                var psi = new System.Diagnostics.ProcessStartInfo(installer, "/S /winpcap_mode=yes")
                {
                    Verb = "runas",
                    UseShellExecute = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
            }
            #region winBlur

            [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }
        internal enum WindowCompositionAttribute
        {
            // ...
            WCA_ACCENT_POLICY = 19
            // ...
        }
        internal enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
            public int BlurRadius;
        }
        

        public void EnableBlur(Window window)
        {
            var windowHelper = new WindowInteropHelper(window);

            var accent = new AccentPolicy();
            var accentStructSize = Marshal.SizeOf(accent);
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        #endregion

        #region corners
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void DwmSetWindowAttribute(IntPtr hwnd,
                                                 DWMWINDOWATTRIBUTE attribute,
                                                 ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute,
                                                 uint cbAttribute);

        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33
        }

        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        public void EnableRoundedCorners(Window window)
        {
            var windowHelper = new WindowInteropHelper(window);
            var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmSetWindowAttribute(windowHelper.Handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(uint));
        }
        #endregion

        public static string ExeDirectory => getDataPath();
        public static string ExePath => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

        static string dir;
        static string getDataPath()
        {
            if (dir is not null) return dir;

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InfinLimit"
            );
            Directory.CreateDirectory(appData);
            return dir = appData;
        }


        private void TransparentWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                EnableBlur(window);
                window.Activated += Window_Activated;
                window.Deactivated += Window_Deactivated;
            }
        }

        public static bool snow = false;
        private void Window_Activated(object? sender, EventArgs e)
        {
            if (sender is Window w)
            {
                if (!App.snow) return;

                var grid = w.Template.FindName("mainddd", w) as Grid;
                if (grid.Children[0] is Snowfall sf)
                {
                    sf._timer.Start();
                }
                else
                {
                    var snow = new Snowfall()
                    {
                        EmissionRate = 5,
                        ParticleSpeed = 0.75,
                        LeaveAnimation = WpfSnowfall.Models.SnowflakeAnimation.Fade,
                        OpacityFactor = 0.55,
                        ScaleFactor = 0.55,
                        Fill = (SolidColorBrush)System.Windows.Application.Current.FindResource("TitleColor")
                    };

                    grid.Children.Insert(0, snow);
                }
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Window w && w.Template.FindName("mainddd", w) is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Snowfall sf)
                {
                    sf._timer.Stop();
                }
            }
            catch (Exception)
            {}
        }

        private void BorderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid b && e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                Window.GetWindow(b).DragMove();
                //var back = b.FindName("screenshot") as Border;
                //BorderChange(win, back);
            }
        }
    }
}
