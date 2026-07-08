using InfinLimit.Interception.Modules;
using InfinLimit.Utility;

using static InfinLimit.Interception.Modules.DevicePriority;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace InfinLimit.Windows.Controls
{
    public partial class ConsoleLimiterPanel : UserControl
    {
        private ObservableCollection<ConsoleDeviceVM> _devices = new();
        private ConsoleModule? _module;
        private DispatcherTimer _speedTimer;

        public ConsoleLimiterPanel()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _devices;
            _devices.CollectionChanged += (_, __) => RefreshEmptyState();
            RefreshEmptyState();

            ConsoleModule.OnStateChanged += () => Dispatcher.Invoke(RefreshState);

            foreach (var profile in ConsoleModule.GameProfiles)
                GameSelector.Items.Add(profile);

            GameSelector.SelectedItem = ConsoleModule.SelectedGame
                ?? ConsoleModule.GameProfiles.Find(g => g.Name == "Destiny 2")
                ?? ConsoleModule.GameProfiles[0];

            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _speedTimer.Tick += (_, __) => UpdateSpeeds();
            _speedTimer.Start();
        }

        public void SetModule(ConsoleModule module)
        {
            _module = module;
            RefreshState();
        }

        private void RefreshState()
        {
            var on = _module?.IsEnabled ?? false;
            ChkEnabled.Checked = on;
            EnabledLabel.Text = on ? "ON" : "OFF";
            EnabledLabel.Foreground = on
                ? (System.Windows.Media.Brush)FindResource("AccentColor")
                : (System.Windows.Media.Brush)FindResource("TextSecondary");
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var active = _devices.Count(d => d.Enabled);
            StatusLabel.Text = _devices.Count == 0
                ? "No consoles — add an IP above"
                : $"{_devices.Count} console(s), {active} active";
        }

        private void UpdateSpeeds()
        {
            foreach (var vm in _devices)
            {
                if (vm.IP == null) continue;
                ConsoleModule.DLSpeedBps.TryGetValue(vm.IP, out var dl);
                ConsoleModule.ULSpeedBps.TryGetValue(vm.IP, out var ul);
                vm.DLSpeedBps = dl;
                vm.ULSpeedBps = ul;
            }
        }

        // ── Enable toggle ────────────────────────────────────────────────────

        private void ChkEnabled_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_module == null) return;
            if (_module.IsEnabled) _module.Disable(); else _module.Enable();
            RefreshState();
        }

        // ── Game selector ────────────────────────────────────────────────────

        private void GameSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (GameSelector.SelectedItem is GameProfile profile)
            {
                ConsoleModule.SelectedGame = profile.Ports.Count == 0 ? null : profile;
                ConsoleModule.SaveGame();
                _module?.RefreshFilter();
            }
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

        private void ApplyLimits_Click(object sender, RoutedEventArgs e)
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

            if (_module != null)
            {
                if (_module.IsEnabled) _module.Disable();
                if (ConsoleModule.Devices.Count > 0)
                    _module.Enable();
                else
                    _module.RefreshArpDevices();
            }

            RefreshState();
            StatusLabel.Text = $"Applied — {ConsoleModule.Devices.Count} console(s)";
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
