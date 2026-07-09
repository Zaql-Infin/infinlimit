using InfinLimit.Interception.Modules;
using InfinLimit.Utility;

using static InfinLimit.Interception.Modules.DevicePriority;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace InfinLimit.Windows.Controls
{
    public partial class ConsoleLimiterPanel : UserControl
    {
        private ObservableCollection<ConsoleDeviceVM> _devices = new();
        private ConsoleModule? _module;

        public ConsoleLimiterPanel()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _devices;
            _devices.CollectionChanged += (_, __) => RefreshEmptyState();
            RefreshEmptyState();

            ConsoleModule.OnStateChanged += () => Dispatcher.Invoke(RefreshState);
        }

        public void SetModule(ConsoleModule module)
        {
            _module = module;
            RefreshState();
        }

        private void RefreshState()
        {
            var on = _module?.IsEnabled ?? false;
            ChkEnabled.SetState(on);
            EnabledLabel.Text = on ? "ON" : "OFF";
            EnabledLabel.Foreground = on
                ? (System.Windows.Media.Brush)FindResource("AccentColor")
                : (System.Windows.Media.Brush)FindResource("TextSecondary");
            PortToggleCB.SetState(ConsoleModule.PortFilterActive);
            PortKeybindBtn.Text = ConsoleModule.PortFilterKeybind.Any()
                ? string.Join(" + ", ConsoleModule.PortFilterKeybind.Select(k => k.ToString().Replace("VK_", "")))
                : "No keybind";
            Port2ToggleCB.SetState(ConsoleModule.Port2FilterActive);
            Port2KeybindBtn.Text = ConsoleModule.Port2FilterKeybind.Any()
                ? string.Join(" + ", ConsoleModule.Port2FilterKeybind.Select(k => k.ToString().Replace("VK_", "")))
                : "No keybind";
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var active = _devices.Count(d => d.Enabled);
            if (_devices.Count == 0)
            {
                StatusLabel.Text = "No consoles — add an IP above";
            }
            else if (_module?.IsEnabled == true)
            {
                StatusLabel.Text = _module.ArpActive
                    ? $"ARP active — {_devices.Count} console(s)"
                    : _module.ArpStatus;
            }
            else
            {
                StatusLabel.Text = $"{_devices.Count} console(s), {active} active";
            }
        }

        // ── Enable toggle ────────────────────────────────────────────────────

        private void ChkEnabled_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_module == null) return;
            if (_module.IsEnabled)
            {
                // Stop all active port filters then disconnect
                ConsoleModule.PortFilterActive  = false;
                if (ConsoleModule.Port2FilterActive) { ConsoleModule.Port2FilterActive = false; _module.StopPort2Filter(); }
                _module.Disable();
            }
            else
            {
                // Connect, then re-apply any port filters that were already toggled on
                ApplyDevices();
                _module.Enable();
                if (ConsoleModule.PortFilterActive)  _module.RefreshFilter();
                if (ConsoleModule.Port2FilterActive) _module.StartPort2Filter();
            }
            RefreshState();
        }

        // ── Device management ────────────────────────────────────────────────

        private void AddDevice_Click(object sender, RoutedEventArgs e)
        {
            var ip = NewDeviceIP.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip)) return;

            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                StatusLabel.Text = "Invalid IP address";
                return;
            }

            var vm = new ConsoleDeviceVM { IP = ip, Name = "Console", Enabled = true };
            vm.PropertyChanged += (_, __) => RefreshEmptyState();
            _devices.Add(vm);
            NewDeviceIP.Text = "";
            RefreshEmptyState();
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ConsoleDeviceVM vm)
            {
                _devices.Remove(vm);
                RefreshEmptyState();
            }
        }

        private async void ScanNetwork_Click(object sender, RoutedEventArgs e)
        {
            ScanProgress.Visibility = Visibility.Visible;
            ScanBar.Value = 0;
            ScanStatus.Text = "Scanning network...";
            (sender as System.Windows.Controls.Button)!.IsEnabled = false;

            try
            {
                var progress = new Progress<int>(p =>
                {
                    ScanBar.Value = p;
                    ScanStatus.Text = $"Scanning... {p}%";
                });

                var found = await ConsoleModule.ScanNetworkAsync(progress);
                ScanStatus.Text = $"Found {found.Count} device(s)";

                if (found.Count > 0)
                {
                    var picker = new ScanResultWindow(found);
                    picker.Owner = Window.GetWindow(this);
                    picker.ShowDialog();
                    if (picker.Selected != null)
                        NewDeviceIP.Text = picker.Selected;
                }
                else
                {
                    ScanStatus.Text = "No devices found on network";
                }
            }
            catch (Exception ex)
            {
                ScanStatus.Text = "Scan failed";
                Logger.Error(ex, "Network scan");
            }
            finally
            {
                (sender as System.Windows.Controls.Button)!.IsEnabled = true;
                await System.Threading.Tasks.Task.Delay(2000);
                ScanProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyDevices()
        {
            ConsoleModule.Devices.Clear();
            foreach (var vm in _devices)
            {
                ConsoleModule.Devices.Add(new ConsoleDevice
                {
                    Name = vm.Name,
                    IP = vm.IP,
                    Enabled = vm.Enabled,
                    UploadKbps = vm.UploadKbps,
                    DownloadKbps = vm.DownloadKbps,
                    Priority = vm.Priority,
                    JitterMs = vm.JitterMs
                });
            }
        }

        // ── Port filter toggles ───────────────────────────────────────────────

        private void PortToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_module == null) return;
            ConsoleModule.PortFilterActive = !ConsoleModule.PortFilterActive;
            if (_module.IsEnabled) SyncModuleToFilters();
            else StatusLabel.Text = "Enable Console Limiter first";
            Config.Save();
            RefreshState();
        }

        private void Port2Toggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_module == null) return;
            ConsoleModule.Port2FilterActive = !ConsoleModule.Port2FilterActive;
            if (_module.IsEnabled)
            {
                if (ConsoleModule.Port2FilterActive) _module.StartPort2Filter();
                else _module.StopPort2Filter();
            }
            else StatusLabel.Text = "Enable Console Limiter first";
            Config.Save();
            RefreshState();
        }

        private void SyncModuleToFilters()
        {
            // Only refresh the running filter — never auto-start or auto-stop the module
            if (_module?.IsEnabled ?? false)
                _module.RefreshFilter();
        }

        // ── Keybind capture ───────────────────────────────────────────────────

        private bool _capturingKeybind = false;
        private InfinLimit.Controls.Button _capturingBtn = null;
        private Action<List<Keycode>> _pendingCapture = null;
        private KeyListener.KeysPressedEventHandler _captureHandler = null;

        private void StartCapture(InfinLimit.Controls.Button btn, Action<List<Keycode>> onCapture)
        {
            if (_capturingKeybind) { StopCapture(); return; }
            _capturingBtn = btn;
            _capturingKeybind = true;
            _pendingCapture = onCapture;
            btn.Text = "Press keys…";
            btn.ButtonBorder.BorderThickness = new System.Windows.Thickness(1.75);
            btn.ButtonBorder.BorderBrush = Brushes.White;
            btn.ButtonBorder.Effect = new DropShadowEffect { ShadowDepth = 0, Color = Colors.White, BlurRadius = 8 };
            _captureHandler = new KeyListener.KeysPressedEventHandler((keys) =>
            {
                if (keys.Count == 1 && keys.First.Value == Keycode.VK_LMB) return;
                var list = new List<Keycode>();
                if (!(keys.Count == 1 && keys.First.Value == Keycode.VK_ESC))
                    list.AddRange(keys);
                _pendingCapture?.Invoke(list);
                Config.Save();
                Dispatcher.Invoke(() => { StopCapture(); RefreshState(); });
            });
            KeyListener.KeysPressed += _captureHandler;
        }

        private void PortKeybind_Click(object sender, RoutedEventArgs e)
        {
            StartCapture(PortKeybindBtn, keys =>
            {
                ConsoleModule.PortFilterKeybind.Clear();
                ConsoleModule.PortFilterKeybind.AddRange(keys);
            });
        }

        private void Port2Keybind_Click(object sender, RoutedEventArgs e)
        {
            StartCapture(Port2KeybindBtn, keys =>
            {
                ConsoleModule.Port2FilterKeybind.Clear();
                ConsoleModule.Port2FilterKeybind.AddRange(keys);
            });
        }

        private void StopCapture()
        {
            if (_captureHandler != null)
            {
                KeyListener.KeysPressed -= _captureHandler;
                _captureHandler = null;
            }
            if (_capturingBtn != null)
            {
                _capturingBtn.ButtonBorder.BorderThickness = new System.Windows.Thickness(0);
                _capturingBtn.ButtonBorder.BorderBrush = Brushes.Transparent;
                _capturingBtn.ButtonBorder.Effect = null;
                _capturingBtn = null;
            }
            _capturingKeybind = false;
            _pendingCapture = null;
        }
    }

    public class ConsoleDeviceVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Notify(string prop) => PropertyChanged?.Invoke(this, new(prop));

        private string _name = "Console";
        private string? _ip;
        private bool _enabled = true;
        private int _uploadKbps = 0;
        private int _downloadKbps = 0;
        private DevicePriority _priority = DevicePriority.Normal;
        private int _jitterMs = 0;
        private long _dlSpeedBps;
        private long _ulSpeedBps;

        public string Name
        {
            get => _name;
            set { _name = value; Notify(nameof(Name)); }
        }
        public string? IP
        {
            get => _ip;
            set { _ip = value; Notify(nameof(IP)); }
        }
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; Notify(nameof(Enabled)); }
        }
        public int UploadKbps
        {
            get => _uploadKbps;
            set { _uploadKbps = value; Notify(nameof(UploadKbps)); }
        }
        public int DownloadKbps
        {
            get => _downloadKbps;
            set { _downloadKbps = value; Notify(nameof(DownloadKbps)); }
        }
        public DevicePriority Priority
        {
            get => _priority;
            set { _priority = value; Notify(nameof(Priority)); }
        }
        public int JitterMs
        {
            get => _jitterMs;
            set { _jitterMs = value; Notify(nameof(JitterMs)); }
        }

        public long DLSpeedBps
        {
            get => _dlSpeedBps;
            set { _dlSpeedBps = value; Notify(nameof(DLSpeedBps)); Notify(nameof(DLSpeedDisplay)); }
        }
        public long ULSpeedBps
        {
            get => _ulSpeedBps;
            set { _ulSpeedBps = value; Notify(nameof(ULSpeedBps)); Notify(nameof(ULSpeedDisplay)); }
        }

        public string DLSpeedDisplay => FormatSpeed(_dlSpeedBps);
        public string ULSpeedDisplay => FormatSpeed(_ulSpeedBps);

        private static string FormatSpeed(long bps)
        {
            if (bps <= 0) return "—";
            if (bps < 1024) return $"{bps} B/s";
            if (bps < 1024 * 1024) return $"{bps / 1024.0:F0} KB/s";
            return $"{bps / (1024.0 * 1024):F1} MB/s";
        }
    }
}
