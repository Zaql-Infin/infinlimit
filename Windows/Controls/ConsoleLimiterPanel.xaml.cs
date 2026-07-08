using InfinLimit.Interception.Modules;
using InfinLimit.Utility;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

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
        }

        public void SetModule(ConsoleModule module)
        {
            _module = module;
        }

        private void RefreshEmptyState()
        {
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var active = 0;
            foreach (var d in _devices) if (d.Enabled) active++;
            StatusLabel.Text = _devices.Count == 0
                ? "No devices — add a console IP above"
                : $"{_devices.Count} device(s) — {active} active";
        }

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
            vm.PropertyChanged += DeviceVM_Changed;
            _devices.Add(vm);
            NewDeviceIP.Text = "";
            RefreshEmptyState();
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ConsoleDeviceVM vm)
            {
                vm.PropertyChanged -= DeviceVM_Changed;
                _devices.Remove(vm);
                RefreshEmptyState();
            }
        }

        private async void ScanNetwork_Click(object sender, RoutedEventArgs e)
        {
            ScanProgress.Visibility = Visibility.Visible;
            ScanBar.Value = 0;
            ScanStatus.Text = "Scanning network...";
            (sender as Button)!.IsEnabled = false;

            try
            {
                var progress = new Progress<int>(p =>
                {
                    ScanBar.Value = p;
                    ScanStatus.Text = $"Scanning... {p}%";
                });

                var found = await ConsoleModule.ScanNetworkAsync(progress);
                ScanStatus.Text = $"Found {found.Count} device(s)";

                // Show discovered IPs in a small popup list
                if (found.Count > 0)
                {
                    var picker = new ScanResultWindow(found);
                    picker.Owner = Window.GetWindow(this);
                    picker.ShowDialog();

                    if (picker.Selected != null)
                    {
                        NewDeviceIP.Text = picker.Selected;
                    }
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
                (sender as Button)!.IsEnabled = true;
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
                    DownloadKbps = vm.DownloadKbps
                });
            }

            if (_module != null)
            {
                if (_module.IsEnabled) _module.Disable();
                if (ConsoleModule.Devices.Count > 0)
                    _module.Enable();
            }

            StatusLabel.Text = $"Applied — {ConsoleModule.Devices.Count} device(s) active";
        }

        private void DeviceVM_Changed(object? sender, PropertyChangedEventArgs e)
        {
            RefreshEmptyState();
        }
    }

    public class ConsoleDeviceVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _name = "Console";
        private string? _ip;
        private bool _enabled = true;
        private int _uploadKbps = 0;
        private int _downloadKbps = 0;

        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        }
        public string? IP
        {
            get => _ip;
            set { _ip = value; PropertyChanged?.Invoke(this, new(nameof(IP))); }
        }
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); }
        }
        public int UploadKbps
        {
            get => _uploadKbps;
            set { _uploadKbps = value; PropertyChanged?.Invoke(this, new(nameof(UploadKbps))); }
        }
        public int DownloadKbps
        {
            get => _downloadKbps;
            set { _downloadKbps = value; PropertyChanged?.Invoke(this, new(nameof(DownloadKbps))); }
        }
    }
}
