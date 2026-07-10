using InfinLimit.Interception;
using InfinLimit.Interception.Modules;
using InfinLimit.Interception.PacketProviders;
using InfinLimit.Utility;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace InfinLimit.Windows
{
    public partial class OverlayWindow : Window
    {
        public struct RECT { public int Left, Top, Right, Bottom; }

        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int GWL_EXSTYLE       = -20;

        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _activeInterval = TimeSpan.FromMilliseconds(500);
        private readonly TimeSpan _idleInterval   = TimeSpan.FromSeconds(1.5);

        // Each module icon: the Path to tint and the Label to show timer text
        private record ModuleIcon(PacketModuleBase Module, Path Icon, Label Timer);
        private readonly List<ModuleIcon> _icons = new();

        private static readonly Brush ActiveBrush   = new SolidColorBrush(Color.FromArgb(0xee, 0xff, 0xff, 0xff));
        private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xff, 0xff, 0xff));
        private static readonly Brush TimerBrush    = new SolidColorBrush(Color.FromArgb(0xcc, 0xff, 0xff, 0xff));

        private static Process  cachedProcess       = null;
        private static bool     LastGameFocusResult = false;
        private static DateTime lastCheckTime       = DateTime.MinValue;

        public static OverlayWindow Current { get; private set; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public  static extern bool   GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] private static extern int    GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int    SetWindowLong(IntPtr hwnd, int index, int style);
        [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyClickThrough();
        }

        public void ApplyClickThrough()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            style |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLong(handle, GWL_EXSTYLE, style);
        }

        public void RefreshModules() => RebuildRows();

        public OverlayWindow()
        {
            InitializeComponent();
            Current = this;
            Topmost = true;

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = _activeInterval };
            _timer.Tick += Tick;
            Closed += (_, __) => { Current = null; _timer.Stop(); };
            Loaded += (_, __) =>
            {
                BuildIcons();
                TryFollowWindow();
                RebuildRows();
                _timer.Start();
            };
        }

        // ── build icon strip ──────────────────────────────────────────────────────

        private void BuildIcons()
        {
            Modules.Children.Clear();
            _icons.Clear();

            var names = new[] { "PVE", "PVP", "З0K", "API Block", "GamePauser" };
            foreach (var name in names)
            {
                var mod = InterceptionManager.GetModule(name);
                if (mod == null) continue;

                var icon = new Path
                {
                    Data            = mod.Icon,
                    Stretch         = Stretch.Uniform,
                    Width           = 28,
                    Height          = 28,
                    Fill            = InactiveBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                icon.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 0,
                    Color       = Colors.Black,
                    BlurRadius  = 4,
                };

                var timerLabel = new Label
                {
                    Content             = "",
                    Foreground          = TimerBrush,
                    FontFamily          = new FontFamily("Bahnschrift Light"),
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 14,
                    Padding             = new Thickness(0),
                    Margin              = new Thickness(2, 0, 8, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Visibility          = Visibility.Collapsed,
                };

                var cell = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    Margin            = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                cell.Children.Add(icon);
                cell.Children.Add(timerLabel);

                Modules.Children.Add(cell);
                _icons.Add(new ModuleIcon(mod, icon, timerLabel));
            }
        }

        // ── update logic ─────────────────────────────────────────────────────────

        private static string FormatElapsed(TimeSpan t)
            => t <= TimeSpan.Zero ? "" : (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"));

        private void RebuildRows()
        {
            foreach (var mi in _icons)
            {
                bool active = mi.Module.IsActivated;
                mi.Icon.Fill = active ? ActiveBrush : InactiveBrush;

                if (active && mi.Module.StartTime != DateTime.MinValue)
                {
                    var elapsed = DateTime.Now - mi.Module.StartTime;
                    var text    = FormatElapsed(elapsed);
                    if (!string.IsNullOrEmpty(text))
                    {
                        mi.Timer.Content    = text;
                        mi.Timer.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        mi.Timer.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    mi.Timer.Visibility = Visibility.Collapsed;
                }
            }

            // Instance timer from 30k provider
            if (InterceptionManager.GetProvider("30000") is _30000_Provider prov30k)
            {
                var dur = prov30k.InstanceDuration();
                if (dur > TimeSpan.Zero)
                {
                    InstanceTimerLabel.Visibility = Visibility.Visible;
                    InstanceTimerLabel.Content    = FormatElapsed(dur);
                }
                else
                {
                    InstanceTimerLabel.Visibility = Visibility.Collapsed;
                }
            }

            // Raid count
            int raids = D2CharacterTracker.RaidsCount;
            RaidStack.Visibility = raids > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (raids > 0)
                RaidLabel.Content = $"{raids} Raid{(raids == 1 ? "" : "s")} today";
        }

        private void Tick(object? sender, EventArgs e)
        {
            if (!CheckGameFocus())
            {
                Visibility = Visibility.Collapsed;
                _timer.Interval = _idleInterval;
                return;
            }

            if (Visibility == Visibility.Collapsed)
            {
                TryFollowWindow();
                this.ElementFadeIn();
                _timer.Interval = _activeInterval;
            }
            else
            {
                TryFollowWindow();
            }

            RebuildRows();
        }

        public void EnsureVisibleNow()
        {
            _timer.Interval = _activeInterval;
            if (Visibility == Visibility.Collapsed)
                this.ElementFadeIn();
            RebuildRows();
        }

        // ── positioning ──────────────────────────────────────────────────────────

        public bool TryFollowWindow()
        {
            if (cachedProcess == null) return false;
            if (!GetWindowRect(cachedProcess.MainWindowHandle, out var rect)) return false;

            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (src?.CompositionTarget == null) return false;

            var m       = src.CompositionTarget.TransformFromDevice;
            var topLeft = m.Transform(new Point(rect.Left, rect.Top));
            var logSize = m.Transform(new Point(rect.Right - rect.Left, rect.Bottom - rect.Top));

            Left   = topLeft.X;
            Top    = topLeft.Y;
            Width  = logSize.X;
            Height = logSize.Y;

            return true;
        }

        // ── game-focus check ─────────────────────────────────────────────────────

        public static bool CheckGameFocus(bool skipCheck = false)
        {
            if (!skipCheck && DateTime.Now - lastCheckTime < TimeSpan.FromSeconds(1))
                return LastGameFocusResult;

            lastCheckTime = DateTime.Now;

            if (cachedProcess == null)
            {
                var procs = Process.GetProcessesByName("destiny2");
                if (!procs.Any()) return LastGameFocusResult = false;
                cachedProcess = procs.First();
            }

            if (cachedProcess.HasExited)
            {
                cachedProcess = null;
                return CheckGameFocus(skipCheck);
            }

            if (GetForegroundWindow() != cachedProcess.MainWindowHandle)
                return LastGameFocusResult = false;

            lastCheckTime += TimeSpan.FromSeconds(1.5);
            return LastGameFocusResult = true;
        }
    }
}
