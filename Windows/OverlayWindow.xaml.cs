using InfinLimit.Controls;
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
using System.Windows.Interop;
using System.Windows.Media;
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

        private readonly List<EnabledModuleTimer> _moduleTimers = new();
        private PincushionEffect _pincushion;

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
            // WS_EX_TOOLWINDOW: hide from Alt-Tab.
            // WS_EX_TRANSPARENT: pass all clicks through to the game beneath.
            // No WS_EX_LAYERED needed — AllowsTransparency handles compositing.
            style |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLong(handle, GWL_EXSTYLE, style);
        }

        public void RefreshModules()
        {
            foreach (var emt in _moduleTimers)
                emt.UpdateTimer();
        }

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
                BuildModuleTimers();
                ApplyPincushion();
                _timer.Start();
            };
        }

        private void BuildModuleTimers()
        {
            Modules.Children.Clear();
            _moduleTimers.Clear();

            // Module strip order mirrors DarkLimiter: PVE, PVP, 30K instance, API, GamePauser
            var names = new[] { "PVE", "PVP", "З0K", "API Block", "GamePauser" };
            foreach (var name in names)
            {
                if (InterceptionManager.GetModule(name) == null) continue;
                var emt = new EnabledModuleTimer(name);
                _moduleTimers.Add(emt);
                Modules.Children.Add(emt);
            }
        }

        private void ApplyPincushion()
        {
            _pincushion = new PincushionEffect { Power = -0.31f };
            Root.Effect = _pincushion;
        }

        // ── update logic ─────────────────────────────────────────────────────────

        private void RebuildRows()
        {
            foreach (var emt in _moduleTimers)
                emt.UpdateTimer();

            // Instance timer from 30k provider
            if (InterceptionManager.GetProvider("30000") is _30000_Provider prov30k)
            {
                var dur = prov30k.InstanceDuration();
                if (dur > TimeSpan.Zero)
                {
                    InstanceTimerLabel.Visibility = Visibility.Visible;
                    InstanceTimerLabel.Content = dur.TotalHours >= 1
                        ? dur.ToString(@"hh\:mm\:ss")
                        : dur.ToString(@"mm\:ss");
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

            // GetWindowRect returns physical pixels; WPF coordinates are logical DIPs.
            var m       = src.CompositionTarget.TransformFromDevice;
            var topLeft = m.Transform(new Point(rect.Left, rect.Top));
            var logSize = m.Transform(new Point(rect.Right - rect.Left, rect.Bottom - rect.Top));

            Left   = topLeft.X;
            Top    = topLeft.Y;
            Width  = logSize.X;
            Height = logSize.Y;

            // Shader expects physical pixel dimensions for correct distortion math
            if (_pincushion != null)
            {
                _pincushion.Width  = (float)(rect.Right  - rect.Left);
                _pincushion.Height = (float)(rect.Bottom - rect.Top);
            }

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
