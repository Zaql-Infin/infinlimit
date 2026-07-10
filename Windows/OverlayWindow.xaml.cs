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
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private struct FeatureRow
        {
            public string Label;
            public Func<bool> EnabledGetter;
            public Func<bool> ActiveGetter;
            public Func<DateTime> SinceGetter;
        }

        private struct DualFeatureRow
        {
            public string Label;
            public Func<bool> EnabledGetter;
            public Func<bool> DlActiveGetter;
            public Func<DateTime> DlSinceGetter;
            public Func<bool> UlActiveGetter;
            public Func<DateTime> UlSinceGetter;
        }

        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int GWL_EXSTYLE = -20;

        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _activeInterval = TimeSpan.FromMilliseconds(500);
        private readonly TimeSpan _idleInterval   = TimeSpan.FromSeconds(1.5);

        private List<FeatureRow>     _featureRows = new();
        private List<DualFeatureRow> _dualRows    = new();

        private Dictionary<string, (Border row, Ellipse dot, Label timer)>                     _rowVisuals  = new();
        private Dictionary<string, (Border row, Ellipse dot, Label dl, Label ul, Label timer)> _dualVisuals = new();

        private Border _instanceRow;
        private Label  _instanceTimer;
        private Border _raidCountRow;
        private Label  _raidCountLabel;
        private Ellipse _raidCountDot;

        private bool _dragging;
        private DateTime _openedAt = DateTime.Now;

        private static Process  cachedProcess        = null;
        private static bool     LastGameFocusResult  = false;
        private static DateTime lastCheckTime        = DateTime.MinValue;

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
            style = Config.Instance.Settings.Overlay_FreePosition
                ? (style & ~WS_EX_TRANSPARENT)
                : (style |  WS_EX_TRANSPARENT);
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
        }

        public OverlayWindow()
        {
            InitializeComponent();
            Current  = this;
            Topmost  = true;

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = _activeInterval };
            _timer.Tick += Tick;
            Closed += (_, __) => { Current = null; _timer.Stop(); };
            Loaded += (_, __) =>
            {
                BuildFeatureRows();
                BuildRowVisuals();
                PositionFree();
                _timer.Start();
            };
        }

        // ── row descriptors ─────────────────────────────────────────────────────

        private void BuildFeatureRows()
        {
            var pve    = InterceptionManager.Modules.OfType<PveModule>().FirstOrDefault();
            var pvp    = InterceptionManager.Modules.OfType<PvpModule>().FirstOrDefault();
            var inst   = InterceptionManager.Modules.OfType<InstanceModule>().FirstOrDefault();
            var api    = InterceptionManager.Modules.OfType<ApiModule>().FirstOrDefault();
            var pauser = InterceptionManager.Modules.OfType<PauserModule>().FirstOrDefault();

            _featureRows.Clear();
            _dualRows.Clear();

            if (pve != null)
                _dualRows.Add(new DualFeatureRow
                {
                    Label          = "3074",
                    EnabledGetter  = () => pve.IsEnabled,
                    DlActiveGetter = () => PveModule.Inbound,
                    DlSinceGetter  = () => pve.StartTime,
                    UlActiveGetter = () => PveModule.Outbound,
                    UlSinceGetter  = () => pve.StartTime,
                });

            if (pvp != null)
                _dualRows.Add(new DualFeatureRow
                {
                    Label          = "27K",
                    EnabledGetter  = () => pvp.IsEnabled,
                    DlActiveGetter = () => PvpModule.Inbound,
                    DlSinceGetter  = () => pvp.StartTime,
                    UlActiveGetter = () => PvpModule.Outbound,
                    UlSinceGetter  = () => pvp.StartTime,
                });

            if (inst != null)
                _featureRows.Add(new FeatureRow
                {
                    Label         = "30K",
                    EnabledGetter = () => inst.IsEnabled,
                    ActiveGetter  = () => inst.IsActivated,
                    SinceGetter   = () => inst.StartTime,
                });

            if (api != null)
                _featureRows.Add(new FeatureRow
                {
                    Label         = "7500",
                    EnabledGetter = () => api.IsEnabled,
                    ActiveGetter  = () => api.IsActivated,
                    SinceGetter   = () => api.StartTime,
                });

            if (pauser != null)
                _featureRows.Add(new FeatureRow
                {
                    Label         = "Game Pauser",
                    EnabledGetter = () => pauser.IsEnabled,
                    ActiveGetter  = () => pauser.IsActivated,
                    SinceGetter   = () => pauser.StartTime,
                });
        }

        // ── build UI rows ────────────────────────────────────────────────────────

        private void BuildRowVisuals()
        {
            Rows.Children.Clear();
            _rowVisuals.Clear();
            _dualVisuals.Clear();

            // ── instance header (timer + divider) ───────────────────────────────
            var instDot = Dot(9);
            var instLbl = new Label { Content = "Instance", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Opacity = 0.9 };
            _instanceTimer = new Label { Content = "", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(10, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
            var instSp = H(instDot, instLbl, _instanceTimer);
            var instDivider = new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 4) };
            var instPanel = new StackPanel(); instPanel.Children.Add(instSp); instPanel.Children.Add(instDivider);
            _instanceRow = new Border { Child = instPanel, Margin = new Thickness(0, 1, 0, 2), Visibility = Visibility.Collapsed };
            Rows.Children.Add(_instanceRow);
            _rowVisuals["__instance__"] = (_instanceRow, instDot, _instanceTimer);

            // ── raid count row (+ divider) ───────────────────────────────────────
            _raidCountDot   = Dot(9);
            _raidCountLabel = new Label { Content = "", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
            var raidSp = H(_raidCountDot, _raidCountLabel);
            var raidDivider = new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 4) };
            var raidPanel = new StackPanel(); raidPanel.Children.Add(raidSp); raidPanel.Children.Add(raidDivider);
            _raidCountRow = new Border { Child = raidPanel, Margin = new Thickness(0, 1, 0, 2), Visibility = Visibility.Collapsed };
            Rows.Children.Add(_raidCountRow);

            // ── dual rows (3074, 27K) ────────────────────────────────────────────
            foreach (var dr in _dualRows)
            {
                var dot    = Dot(7);
                var name   = NameLbl(dr.Label);
                var dlTag  = DirTag("DL");
                var ulTag  = DirTag("UL");
                var tmr    = TimerLbl();
                var border = Row(H(dot, name, dlTag, ulTag, tmr));
                Rows.Children.Add(border);
                _dualVisuals[dr.Label] = (border, dot, dlTag, ulTag, tmr);
            }

            // ── single rows (30K, 7500, Game Pauser) ────────────────────────────
            foreach (var fr in _featureRows)
            {
                var dot    = Dot(7);
                var name   = NameLbl(fr.Label);
                var tmr    = TimerLbl();
                var border = Row(H(dot, name, tmr));
                Rows.Children.Add(border);
                _rowVisuals[fr.Label] = (border, dot, tmr);
            }

            // helpers
            static Ellipse Dot(double size) => new Ellipse { Width = size, Height = size, Fill = Brushes.Gray, Margin = new Thickness(0, 0, size < 9 ? 6 : 8, 0), VerticalAlignment = VerticalAlignment.Center };
            static Label NameLbl(string text) => new Label { Content = text, Foreground = Brushes.White, FontSize = 12, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            static Label DirTag(string text) => new Label { Content = text, FontSize = 9, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Opacity = 0.4 };
            static Label TimerLbl() => new Label { Content = "", Foreground = Brushes.White, FontSize = 11, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, Opacity = 0.6 };
            static StackPanel H(params UIElement[] children) { var sp = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) sp.Children.Add(c); return sp; }
            static Border Row(UIElement content) => new Border { Child = content, Margin = new Thickness(0, 1, 0, 1), Visibility = Visibility.Collapsed };
        }

        // ── update logic ─────────────────────────────────────────────────────────

        private static string FormatElapsed(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return "";
            return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        private void RebuildRows()
        {
            var accent = (Application.Current.Resources["AccentColor"] as SolidColorBrush) ?? Brushes.LimeGreen;

            // instance header
            var xbox = InterceptionManager.GetProvider("Xbox") as XboxProvider;
            if (xbox != null)
            {
                var dur = xbox.InstanceDuration();
                bool show = dur > TimeSpan.Zero;
                _instanceRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    _rowVisuals["__instance__"].dot.Fill = accent;
                    _instanceTimer.Content = FormatElapsed(dur);
                }
            }

            // raid count
            int raids = D2CharacterTracker.RaidsCount;
            _raidCountRow.Visibility = raids > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (raids > 0)
            {
                _raidCountLabel.Content = $"Today: {raids} Raid{(raids == 1 ? "" : "s")}";
                _raidCountDot.Fill = accent;
            }

            // dual rows — always visible, dimmed when disabled
            foreach (var dr in _dualRows)
            {
                if (!_dualVisuals.TryGetValue(dr.Label, out var v)) continue;
                bool enabled = dr.EnabledGetter();
                v.row.Visibility = Visibility.Visible;
                v.row.Opacity    = enabled ? 1.0 : 0.35;

                bool dlOn = enabled && dr.DlActiveGetter();
                bool ulOn = enabled && dr.UlActiveGetter();
                v.dot.Fill      = (dlOn || ulOn) ? accent : Brushes.Gray;
                v.dl.Foreground = dlOn ? accent : Brushes.Gray; v.dl.Opacity = dlOn ? 1.0 : 0.4;
                v.ul.Foreground = ulOn ? accent : Brushes.Gray; v.ul.Opacity = ulOn ? 1.0 : 0.4;

                DateTime since = DateTime.MinValue;
                if (dlOn) since = dr.DlSinceGetter();
                if (ulOn) { var s2 = dr.UlSinceGetter(); if (since == DateTime.MinValue || s2 < since) since = s2; }
                v.timer.Content = FormatElapsed(since != DateTime.MinValue ? DateTime.Now - since : TimeSpan.Zero);
            }

            // single rows — always visible, dimmed when disabled
            foreach (var fr in _featureRows)
            {
                if (!_rowVisuals.TryGetValue(fr.Label, out var v)) continue;
                bool enabled = fr.EnabledGetter();
                v.row.Visibility = Visibility.Visible;
                v.row.Opacity    = enabled ? 1.0 : 0.35;

                bool on = enabled && fr.ActiveGetter();
                v.dot.Fill      = on ? accent : Brushes.Gray;
                var since       = fr.SinceGetter();
                v.timer.Content = FormatElapsed(on && since != DateTime.MinValue ? DateTime.Now - since : TimeSpan.Zero);
            }

            InvalidateMeasure();
            UpdateLayout();
        }

        private void Tick(object? sender, EventArgs e)
        {
            bool inGrace = DateTime.Now - _openedAt < TimeSpan.FromSeconds(4);
            if (!inGrace && !Config.Instance.Settings.Overlay_FreePosition && !CheckGameFocus())
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

        // ── positioning ──────────────────────────────────────────────────────────

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Config.Instance.Settings.Overlay_FreePosition) return;
            _dragging = true;
            DragMove();
            _dragging = false;
            ClampToScreen();
            Config.Instance.Settings.Overlay_FreeX = Left;
            Config.Instance.Settings.Overlay_FreeY = Top;
            Config.Save();
        }

        private void PositionFree()
        {
            Left = Config.Instance.Settings.Overlay_FreeX;
            Top  = Config.Instance.Settings.Overlay_FreeY;
            ClampToScreen();
        }

        public bool TryFollowWindow()
        {
            if (Config.Instance.Settings.Overlay_FreePosition) return true;
            if (cachedProcess == null) return false;
            if (!GetWindowRect(cachedProcess.MainWindowHandle, out var rect)) return false;

            // GetWindowRect returns physical pixels; WPF Left/Top are logical DIP units.
            // Convert to avoid off-screen placement on scaled displays.
            var src = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (src?.CompositionTarget == null) return false;
            var m = src.CompositionTarget.TransformFromDevice;
            var logical = m.Transform(new System.Windows.Point(rect.Left, rect.Bottom));

            Left = logical.X + 24 + Config.Instance.Settings.Overlay_LeftOffset;
            Top  = logical.Y - 140 - Config.Instance.Settings.Overlay_BottomOffset;
            ClampToScreen();
            return true;
        }

        private void ClampToScreen()
        {
            var wa = SystemParameters.WorkArea;
            if (Left < wa.Left) Left = wa.Left;
            if (Top  < wa.Top)  Top  = wa.Top;
            if (Left + ActualWidth  > wa.Right)  Left = Math.Max(wa.Left, wa.Right  - ActualWidth);
            if (Top  + ActualHeight > wa.Bottom) Top  = Math.Max(wa.Top,  wa.Bottom - ActualHeight);
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

            GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);
            if (fgPid != (uint)cachedProcess.Id)
                return LastGameFocusResult = false;

            lastCheckTime += TimeSpan.FromSeconds(1.5);
            return LastGameFocusResult = true;
        }
    }
}
