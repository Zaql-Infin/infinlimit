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

        private record ModuleIcon(
            PacketModuleBase Module,
            Path   Icon,
            Border Circle,
            Label  DlArrow,   // ↓ download indicator
            Label  UlArrow,   // ↑ upload indicator
            Label  Timer
        );
        private readonly List<ModuleIcon> _icons = new();

        private static readonly Brush ActiveBrush       = new SolidColorBrush(Color.FromArgb(0xee, 0xff, 0xff, 0xff));
        private static readonly Brush InactiveBrush     = new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0xff, 0xff));
        private static readonly Brush ActiveRingBrush   = new SolidColorBrush(Color.FromArgb(0xaa, 0xff, 0xff, 0xff));
        private static readonly Brush InactiveRingBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xff, 0xff, 0xff));
        private static readonly Brush ActiveBgBrush     = new SolidColorBrush(Color.FromArgb(0x28, 0xff, 0xff, 0xff));
        private static readonly Brush InactiveBgBrush   = new SolidColorBrush(Color.FromArgb(0x10, 0xff, 0xff, 0xff));
        private static readonly Brush ArrowBrush        = new SolidColorBrush(Color.FromArgb(0xcc, 0xff, 0xff, 0xff));
        private static readonly Brush TimerBrush        = new SolidColorBrush(Color.FromArgb(0xaa, 0xff, 0xff, 0xff));

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

        private static Label MakeInfoLabel(string text) => new Label
        {
            Content             = text,
            Foreground          = ArrowBrush,
            FontFamily          = new FontFamily("Segoe UI"),
            FontSize            = 11,
            FontWeight          = FontWeights.Bold,
            Padding             = new Thickness(0),
            VerticalContentAlignment   = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Visibility          = Visibility.Collapsed,
        };

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
                    Data                = mod.Icon,
                    Stretch             = Stretch.Uniform,
                    Width               = 22,
                    Height              = 22,
                    Fill                = InactiveBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                };

                var circle = new Border
                {
                    Width           = 46,
                    Height          = 46,
                    CornerRadius    = new CornerRadius(23),
                    BorderBrush     = InactiveRingBrush,
                    BorderThickness = new Thickness(1.5),
                    Background      = InactiveBgBrush,
                    Child           = icon,
                };
                circle.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 0, Color = Colors.Black, BlurRadius = 6, Opacity = 0.5,
                };

                // ↓ download arrow
                var dlArrow = MakeInfoLabel("↓");
                // ↑ upload arrow
                var ulArrow = MakeInfoLabel("↑");
                // elapsed timer
                var timer = new Label
                {
                    Foreground          = TimerBrush,
                    FontFamily          = new FontFamily("Bahnschrift Light"),
                    FontSize            = 11,
                    FontWeight          = FontWeights.Bold,
                    Padding             = new Thickness(0),
                    VerticalContentAlignment   = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Visibility          = Visibility.Collapsed,
                };

                // row below the circle: [↓] [↑] [timer]
                var infoRow = new StackPanel
                {
                    Orientation         = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 3, 0, 0),
                };
                infoRow.Children.Add(dlArrow);
                infoRow.Children.Add(ulArrow);
                infoRow.Children.Add(timer);

                var cell = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin      = new Thickness(3, 0, 3, 0),
                };
                cell.Children.Add(circle);
                cell.Children.Add(infoRow);

                Modules.Children.Add(cell);
                _icons.Add(new ModuleIcon(mod, icon, circle, dlArrow, ulArrow, timer));
            }
        }

        // ── update logic ─────────────────────────────────────────────────────────

        private static string FormatElapsed(TimeSpan t)
            => t <= TimeSpan.Zero ? "" : (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"));

        private void RebuildRows()
        {
            foreach (var mi in _icons)
            {
                bool dl = false, ul = false;
                DateTime since = DateTime.MinValue;

                if (mi.Module is PveModule pve)
                {
                    dl    = PveModule.Inbound  || PveModule.SlowInbound;
                    ul    = PveModule.Outbound || PveModule.SlowOutbound;
                    since = pve.StartTime;
                }
                else if (mi.Module is PvpModule pvp)
                {
                    dl    = PvpModule.Inbound;
                    ul    = PvpModule.Outbound;
                    since = pvp.StartTime;
                }
                else
                {
                    dl    = mi.Module.IsActivated;
                    since = mi.Module.StartTime;
                }

                bool active = dl || ul;

                mi.Icon.Fill        = active ? ActiveBrush       : InactiveBrush;
                mi.Circle.BorderBrush = active ? ActiveRingBrush  : InactiveRingBrush;
                mi.Circle.Background  = active ? ActiveBgBrush    : InactiveBgBrush;

                // arrows — only show for PVE/PVP (bidirectional); for others just DL arrow acts as "on"
                bool isBidirectional = mi.Module is PveModule or PvpModule;
                mi.DlArrow.Visibility = dl ? Visibility.Visible : Visibility.Collapsed;
                mi.UlArrow.Visibility = (isBidirectional && ul) ? Visibility.Visible : Visibility.Collapsed;
                // add spacing between arrows when both arrows show
                mi.DlArrow.Margin = (isBidirectional && dl && ul) ? new Thickness(0, 0, 2, 0) : new Thickness(0);

                // timer
                if (active && since != DateTime.MinValue)
                {
                    var text = FormatElapsed(DateTime.Now - since);
                    if (!string.IsNullOrEmpty(text))
                    {
                        mi.Timer.Content    = text;
                        mi.Timer.Margin     = (dl || ul) ? new Thickness(3, 0, 0, 0) : new Thickness(0);
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

            Left = topLeft.X;
            Top  = topLeft.Y;

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
