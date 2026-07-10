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
using System.Windows.Input;
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
            Path         Icon,
            Border       Circle,
            Label        DlArrow,    // ↓ download indicator
            Label        UlArrow,    // ↑ upload indicator
            Label        Timer,
            StackPanel   InfoPanel   // right-side panel (arrows + timer)
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
                    Width           = 44,
                    Height          = 44,
                    CornerRadius    = new CornerRadius(22),
                    BorderBrush     = InactiveRingBrush,
                    BorderThickness = new Thickness(1.5),
                    Background      = InactiveBgBrush,
                    Child           = icon,
                };
                circle.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 0, Color = Colors.Black, BlurRadius = 6, Opacity = 0.5,
                };

                var dlArrow = MakeInfoLabel("↓");
                var ulArrow = MakeInfoLabel("↑");

                var timer = new Label
                {
                    Foreground                 = TimerBrush,
                    FontFamily                 = new FontFamily("Consolas"),
                    FontSize                   = 13,
                    FontWeight                 = FontWeights.Normal,
                    Padding                    = new Thickness(0),
                    VerticalContentAlignment   = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Visibility                 = Visibility.Collapsed,
                };

                // arrows stacked vertically to the right of the circle
                var arrowStack = new StackPanel
                {
                    Orientation       = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(4, 0, 2, 0),
                };
                arrowStack.Children.Add(ulArrow);  // ↑ on top
                arrowStack.Children.Add(dlArrow);  // ↓ on bottom

                // info panel: arrows | timer — hidden until active
                var infoPanel = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility        = Visibility.Collapsed,
                };
                infoPanel.Children.Add(arrowStack);
                infoPanel.Children.Add(timer);

                // cell: [circle] [info]
                var cell = new StackPanel
                {
                    Orientation       = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(3, 0, 3, 0),
                };
                cell.Children.Add(circle);
                cell.Children.Add(infoPanel);

                Modules.Children.Add(cell);
                _icons.Add(new ModuleIcon(mod, icon, circle, dlArrow, ulArrow, timer, infoPanel));
            }
        }

        // ── update logic ─────────────────────────────────────────────────────────

        private static string FormatElapsed(TimeSpan t)
            => t <= TimeSpan.Zero ? "" : (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"));
        // zero-padded mm:ss like "00:01"
        private static string FormatTimer(TimeSpan t)
            => t <= TimeSpan.Zero ? "" : (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}");

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

                mi.Icon.Fill          = active ? ActiveBrush      : InactiveBrush;
                mi.Circle.BorderBrush = active ? ActiveRingBrush  : InactiveRingBrush;
                mi.Circle.Background  = active ? ActiveBgBrush    : InactiveBgBrush;

                // arrows — only for PVE/PVP (bidirectional)
                bool isBidirectional = mi.Module is PveModule or PvpModule;
                mi.UlArrow.Visibility = (isBidirectional && ul) ? Visibility.Visible : Visibility.Collapsed;
                mi.DlArrow.Visibility = (isBidirectional && dl) ? Visibility.Visible : Visibility.Collapsed;

                // timer (mm:ss zero-padded)
                if (active && since != DateTime.MinValue)
                {
                    var text = FormatTimer(DateTime.Now - since);
                    if (!string.IsNullOrEmpty(text))
                    {
                        mi.Timer.Content    = text;
                        mi.Timer.Margin     = isBidirectional ? new Thickness(4, 0, 0, 0) : new Thickness(0);
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

                // show/hide the entire right-side info panel
                mi.InfoPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
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
            // While dragging: keep the overlay visible and skip all repositioning
            if (_dragHandler != null)
            {
                if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
                RebuildRows();
                return;
            }

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

        // ── drag / free-position ─────────────────────────────────────────────────

        private MouseButtonEventHandler _dragHandler;

        public void EnableDrag()
        {
            // Make sure the overlay is on screen before the user tries to drag it
            Visibility = Visibility.Visible;
            Activate();

            // Remove WS_EX_TRANSPARENT so the window receives mouse input
            var handle = new WindowInteropHelper(this).Handle;
            int style  = GetWindowLong(handle, GWL_EXSTYLE);
            style &= ~WS_EX_TRANSPARENT;
            SetWindowLong(handle, GWL_EXSTYLE, style);

            _dragHandler = (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    Activate();
                    DragMove();
                }
            };
            MouseLeftButtonDown += _dragHandler;

            Config.Instance.Settings.Overlay_FreePosition = true;
        }

        public void DisableDrag()
        {
            if (_dragHandler != null)
            {
                MouseLeftButtonDown -= _dragHandler;
                _dragHandler = null;
            }

            // Save current position then re-apply click-through
            Config.Instance.Settings.Overlay_FreeX = Left;
            Config.Instance.Settings.Overlay_FreeY = Top;
            Config.Save();

            ApplyClickThrough();
        }

        // ── positioning ──────────────────────────────────────────────────────────

        public bool TryFollowWindow()
        {
            // Free-position mode: stay where the user dragged it
            if (Config.Instance.Settings.Overlay_FreePosition)
            {
                Left = Config.Instance.Settings.Overlay_FreeX;
                Top  = Config.Instance.Settings.Overlay_FreeY;
                return true;
            }

            if (cachedProcess == null) return false;
            if (!GetWindowRect(cachedProcess.MainWindowHandle, out var rect)) return false;

            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (src?.CompositionTarget == null) return false;

            var m       = src.CompositionTarget.TransformFromDevice;
            var topLeft = m.Transform(new Point(rect.Left, rect.Top));

            Left = topLeft.X + Config.Instance.Settings.Overlay_LeftOffset;
            Top  = topLeft.Y + Config.Instance.Settings.Overlay_BottomOffset;

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
