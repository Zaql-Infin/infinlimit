using InfinLimit.Interception.Modules;
using InfinLimit.Utility;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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

        private Dictionary<InfinLimit.Controls.Button, List<Keycode>> _listening = new();
        private DateTime _lastUpdated = DateTime.MinValue;
        private SemaphoreSlim _keybindSemaphore = new(1);

        public ConsoleLimiterPanel()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _devices;
            _devices.CollectionChanged += (_, __) => RefreshEmptyState();
            RefreshEmptyState();

            ConsoleModule.OnStateChanged += () => Dispatcher.Invoke(RefreshCheckboxes);
        }

        public void SetModule(ConsoleModule module)
        {
            _module = module;
            RefreshCheckboxes();
        }

        private void RefreshCheckboxes()
        {
            ChkEnabled.Checked = _module?.IsEnabled ?? false;
            ChkDL.Checked = ConsoleModule.DLEnabled;
            ChkUL.Checked = ConsoleModule.ULEnabled;
            ChkDLSlow.Checked = ConsoleModule.DLSlowEnabled;
            ChkULSlow.Checked = ConsoleModule.ULSlowEnabled;
            ChkAutoResync.Checked = ConsoleModule.AutoResync;
            ChkBuffering.Checked = ConsoleModule.Buffering;

            SetBindText(BindEnabled,    ConsoleModule.EnableKeybind);
            SetBindText(BindDL,         ConsoleModule.DLKeybind);
            SetBindText(BindUL,         ConsoleModule.ULKeybind);
            SetBindText(BindDLSlow,     ConsoleModule.DLSlowKeybind);
            SetBindText(BindULSlow,     ConsoleModule.ULSlowKeybind);
            SetBindText(BindAutoResync, ConsoleModule.AutoResyncKeybind);
            SetBindText(BindBuffering,  ConsoleModule.BufferingKeybind);
        }

        private static void SetBindText(InfinLimit.Controls.Button btn, List<Keycode> bind)
        {
            btn.Text = bind.Count == 0
                ? "No keybind"
                : string.Join(" + ", bind.Select(k => k.ToString().Replace("VK_", "")));
        }

        private void RefreshEmptyState()
        {
            EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var active = 0;
            foreach (var d in _devices) if (d.Enabled) active++;
            StatusLabel.Text = _devices.Count == 0
                ? "No consoles — add an Xbox / PlayStation IP above"
                : $"{_devices.Count} console(s), {active} active";
        }

        // ── Keybind capture ──────────────────────────────────────────────────

        private void KeybindButtonClick(object sender, RoutedEventArgs e)
        {
            if (DateTime.Now - _lastUpdated <= TimeSpan.FromSeconds(0.15) || _keybindSemaphore.CurrentCount == 0)
                return;

            var button = (InfinLimit.Controls.Button)sender;
            _keybindSemaphore.Wait();

            bool listen = !_listening.ContainsKey(button);
            if (listen)
            {
                List<Keycode>? bind = button.Name switch
                {
                    nameof(BindEnabled)    => ConsoleModule.EnableKeybind,
                    nameof(BindDL)         => ConsoleModule.DLKeybind,
                    nameof(BindUL)         => ConsoleModule.ULKeybind,
                    nameof(BindDLSlow)     => ConsoleModule.DLSlowKeybind,
                    nameof(BindULSlow)     => ConsoleModule.ULSlowKeybind,
                    nameof(BindAutoResync) => ConsoleModule.AutoResyncKeybind,
                    nameof(BindBuffering)  => ConsoleModule.BufferingKeybind,
                    _                      => null
                };
                if (bind == null) { _keybindSemaphore.Release(); return; }

                _listening.Add(button, bind);

                if (_listening.Count == 1)
                {
                    _module?.UnhookKeybind();
                    KeyListener.KeysPressed += ListeningNewKeybind;
                }

                button.ButtonBorder.BorderThickness = new Thickness(1.75);
                button.ButtonBorder.BorderBrush = Brushes.White;
                button.ButtonBorder.Effect = new DropShadowEffect { ShadowDepth = 0, Color = Colors.White, BlurRadius = 8 };
            }
            else
            {
                _listening.Remove(button);

                if (_listening.Count == 0)
                {
                    KeyListener.KeysPressed -= ListeningNewKeybind;
                    _module?.RehookKeybind();
                }

                button.ButtonBorder.BorderThickness = new Thickness(0);
                button.ButtonBorder.BorderBrush = Brushes.Transparent;
                button.ButtonBorder.Effect = null;

                ConsoleModule.SaveKeybinds();
            }

            _keybindSemaphore.Release();
            _lastUpdated = DateTime.Now;
        }

        private void ListeningNewKeybind(LinkedList<Keycode> keycodes)
        {
            if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_LMB)
                return;

            foreach (var b in _listening.Values)
                b.Clear();

            if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_ESC)
            {
                Dispatcher.Invoke(DispatcherPriority.Background, () =>
                {
                    foreach (var b in _listening.Keys)
                        b.Text = "No keybind";
                });
                return;
            }

            foreach (var b in _listening.Values)
                b.AddRange(keycodes);

            Dispatcher.Invoke(DispatcherPriority.Background, () =>
            {
                try
                {
                    foreach (var b in _listening)
                        b.Key.Text = string.Join(" + ", b.Value.Select(k => k.ToString().Replace("VK_", "")));
                }
                catch { }
            });
        }

        // ── Checkbox direct-click toggles ────────────────────────────────────

        private void ChkEnabled_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_module == null) return;
            if (_module.IsEnabled) _module.Disable(); else _module.Enable();
            RefreshCheckboxes();
        }

        private void ChkDL_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.DLEnabled = !ConsoleModule.DLEnabled;
            if (ConsoleModule.DLEnabled) ConsoleModule.DLSlowEnabled = false;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
        }

        private void ChkUL_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.ULEnabled = !ConsoleModule.ULEnabled;
            if (ConsoleModule.ULEnabled) ConsoleModule.ULSlowEnabled = false;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
        }

        private void ChkDLSlow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.DLSlowEnabled = !ConsoleModule.DLSlowEnabled;
            if (ConsoleModule.DLSlowEnabled) ConsoleModule.DLEnabled = false;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
        }

        private void ChkULSlow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.ULSlowEnabled = !ConsoleModule.ULSlowEnabled;
            if (ConsoleModule.ULSlowEnabled) ConsoleModule.ULEnabled = false;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
        }

        private void ChkAutoResync_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.AutoResync = !ConsoleModule.AutoResync;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
        }

        private void ChkBuffering_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ConsoleModule.Buffering = !ConsoleModule.Buffering;
            ConsoleModule.SaveFlags();
            ConsoleModule.OnStateChanged?.Invoke();
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
            vm.PropertyChanged += DeviceVM_Changed;
            _devices.Add(vm);
            NewDeviceIP.Text = "";
            RefreshEmptyState();
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ConsoleDeviceVM vm)
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
                    DownloadKbps = vm.DownloadKbps
                });
            }

            if (_module != null)
            {
                if (_module.IsEnabled) _module.Disable();
                if (ConsoleModule.Devices.Count > 0)
                    _module.Enable();
            }

            StatusLabel.Text = $"Applied — {ConsoleModule.Devices.Count} console(s) active";
        }

        private void DeviceVM_Changed(object? sender, PropertyChangedEventArgs e) => RefreshEmptyState();
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
