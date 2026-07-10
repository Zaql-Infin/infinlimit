using Hardcodet.Wpf.TaskbarNotification;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.Logging;

using InfinLimit.Controls;
using InfinLimit.Database;
using InfinLimit.Interception;
using InfinLimit.Interception.Modules;
using InfinLimit.Models;
using InfinLimit.Utility;
using InfinLimit.Windows;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using WindivertDotnet;
using InfinLimit.Utility;

using Application = System.Windows.Application;

namespace InfinLimit
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private double _iconThickness = 1.0;
        public double IconThickness
        {
            get => _iconThickness;
            set
            {
                if (_iconThickness != value)
                {
                    _iconThickness = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconThickness)));
                }
            }
        }

        private Geometry _pathDataProperty = Geometry.Parse("M 0 0 L 10 0 L 10 10 L 0 10 Z");
        public Geometry PathDataProperty
        {
            get => _pathDataProperty;
            set
            {
                if (_pathDataProperty != value)
                {
                    _pathDataProperty = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PathDataProperty)));
                }
            }
        }
        public static MainWindow Instance { get; private set; }
        public IdentityChecker Checker { get; private set; }
        public DateTime AppStart = DateTime.Now;
        TimeSpan animationTime = TimeSpan.FromSeconds(0.5);
        
        public ObservableCollection<EnabledModuleTimer> DisplayModules { get; set; } = new ObservableCollection<EnabledModuleTimer>();
        KeyListener inputListener { get; set; }  


        

        public string CurrentModuleName => Config.Instance.CurrentModule;
        private PacketModuleBase CurrentModule => InterceptionManager.GetModule(CurrentModuleName);
        private List<Keycode> Keybind => Config.GetNamed(CurrentModuleName).Keybind;
        private int _currentSwapProfile = 0;
        private bool _suppressSwapperUI = false;


        public OverlayWindow overlay { get; set; }
        private ConsoleModule _consoleModule;

        public void KeyLogger(LinkedList<Keycode> keycodes) => Logger.Key(String.Join(" + ", keycodes.Select(x => x.ToString().Replace("VK_", ""))));

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        public MainWindow(IdentityChecker auth)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += FirstChanceExceptionHandler;
            AppDomain.CurrentDomain.ProcessExit += ProcessExitHandler;

            Logger.Debug(App.ExeDirectory);

            Checker = auth;
            // DEBUG
            Task.Run(() => ExtraLogger.Login());

            Instance = this;
            DataContext = this;
            Title = Checker.Name;
            
            try
            {
                InitializeComponent();
                VersionBadge.Text = Updater.Version.ToString();
                Logger.Info("InitializeComponent completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Fatal($"InitializeComponent failed: {ex}");
                throw; // Re-throw to preserve original exception
            }
            
            Logger.Info("App started");

            inputListener = new KeyListener();
            if (Config.Instance.Settings.DB_KeyPresses)
                KeyListener.KeysPressed += KeyLogger;

            KeyListener.KeysPressed += AltTabTracker;
            InterceptionManager.Init();
            AhkManager.Init();

            // Wire up console module
            _consoleModule = new ConsoleModule();
            ConsolePanelControl.SetModule(_consoleModule);

            // Auto-update check via Velopack
            _ = Task.Run(AppUpdater.CheckForUpdatesAsync);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                try
                {
                    StartupProgressBar.Instance?.Close();
                    Logger.Info("StartupProgressBar closed successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error closing StartupProgressBar: {ex}");
                }

                float getBrightness(System.Drawing.Color c)
                { 
                    return (c.R * 0.299f + c.G * 0.587f + c.B * 0.114f) / 256f; 
                }

                CurrentTime.Content = DateTime.Now.ToString("hh:mm:ss");

                if (!Config.Instance.Settings.Window_DisplayClock)
                    CurrentTime.Visibility = Visibility.Collapsed;
                if (!Config.Instance.Settings.Window_DisplaySpeed)
                    Speed.Visibility = Visibility.Collapsed;
                if (Checker.Type < IdentityChecker.AccessType.Debug)
                    OpenLogs.Visibility = Visibility.Collapsed;
                if (Config.Instance.Settings.Overlay_StartOnLaunch)
                {
                    OverlayClick(null, null);
                    Overlay.Border_MouseEnter(this, null);
                    Overlay.Border_MouseLeave(this, null);
                }

                CaptureGuard.Apply(this);
                    

                SolidColorBrush accent;
                try
                {
                    accent = System.Windows.Application.Current.Resources["AccentColor"] as SolidColorBrush;
                    if (accent == null)
                    {
                        Logger.Error("AccentColor resource not found or not a SolidColorBrush");
                        accent = new SolidColorBrush(Colors.Crimson); // Default fallback
                    }
                    Logger.Info("AccentColor resource accessed successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error accessing AccentColor resource: {ex}");
                    accent = new SolidColorBrush(Colors.Crimson); // Default fallback
                }

                System.Drawing.Color color;
                SolidColorBrush dimmedAccent;
                
                try
                {
                    color = System.Drawing.Color.FromArgb(accent.Color.R, accent.Color.G, accent.Color.B);
                    float hue = color.GetHue();
                    float saturation = color.GetSaturation();
                    float lightness = getBrightness(color) - 0.425f;
                    dimmedAccent = new SolidColorBrush(ExtensionMethods.ColorFromHSL(hue, saturation, lightness));
                    Logger.Info("Color calculations completed successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error in color calculations: {ex}");
                    dimmedAccent = new SolidColorBrush(Colors.DarkRed);
                }

                int i = 0;
                int j = 0;

                try
                {
                    Logger.Info($"Starting module button creation. Module count: {InterceptionManager.Modules?.Count ?? 0}");
                    
                    var allModules = InterceptionManager.Modules ?? new List<PacketModuleBase>();
                    Logger.Info($"Available modules: {string.Join(", ", allModules.Select(m => m.Name))}");
                    
                    foreach (var m in allModules)
                {
                    if (m.Name == "Solo" || m.Name == "Timer" || m.Name == "Test" || m.Name == "Res" || m.Name == "Multishot")
                        continue;

                
                    var button = new WindowControlButton()
                    {
                        //GlowColor = m.Color,
                        FillColor = accent,
                        GlowColor = accent,
                        PathData = m.Icon,
                        Name = m.Name.Replace(" ", "_"),
                        stayActive = m.IsEnabled
                    };

                    button.Click += NewModuleClicked;
                    RegisterName(button.Name, button);
                    ModuleSelection.Children.Add(button);
                    Grid.SetRow(button, j);
                    Grid.SetColumn(button, i);

                        i++;
                        if (i == 4)
                        {
                            i = 0;
                            j++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error creating module buttons: {ex}");
                }

                try
                {
                    VolumeSlider.Value = Config.Instance.Volume;
                    UpdateSelectedModule();
                    Logger.Info("Volume slider and module selection updated successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error setting volume slider and updating module selection: {ex}");
                }

                try
                {
                    var t = new DispatcherTimer();
                    t.Tick += WindowTick;
                    t.Interval = TimeSpan.FromSeconds(0.5);
                    t.Start();

                    var l = new DispatcherTimer();
                    l.Tick += (s, a) => Task.Run(async () =>
                    {
                     //   Checker.AuthApp.check();
                        var a = Checker.Calc;
                    });
                    l.Interval = TimeSpan.FromSeconds(120);
                    l.Start();
                    
                    Logger.Info("Timers started successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error starting timers: {ex}");
                }
            });

        }

        private void AltTabTracker(LinkedList<Keycode> keycodes)
        {
            if (keycodes.Contains(Keycode.VK_LWIN) && keycodes.Contains(Keycode.VK_TAB))
            {
                OverlayWindow.CheckGameFocus(true);
            }
        }
        

        private void ProcessExitHandler(object? sender, EventArgs e)
        {
            try
            {
                AhkManager.ReloadAhksFromDirectory();
                Config.Instance.LastOpenAhks.Clear();
                Config.Instance.LastOpenAhks.AddRange(AhkManager.Ahks.Where(x => x.Value > 0).Select(x => x.Key));
                Config.Save();
                if (Config.Instance.Settings.AHK_AutoClose)
                {
                    foreach (var ahk in Config.Instance.LastOpenAhks)
                        AhkManager.TryStopAhk(ahk);
                }
             //   Checker.AuthApp.logout();
            }
            catch 
            {
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ExtraLogger.Error(e.ExceptionObject as Exception, "Unhandled");
        }

        private void FirstChanceExceptionHandler(object sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is TaskCanceledException tc)
            {
                Logger.Debug($"Task cancelled");
                return;
            }
            Logger.Debug($"{e.Exception.GetType().Name}: {e.Exception.StackTrace}");
        }

        private void WindowTick(object? sender, EventArgs e)
        {
            // Swapper status dot
            try
            {
                var swapper = InterceptionManager.GetModule("Swapper") as SwapperModule;
                if (swapper != null && SWAPPER_Panel.Visibility == Visibility.Visible)
                {
                    bool active = swapper.IsActivated;
                    SwapperStatusLabel.Text = active ? "Swapping" : "Idle";
                    SwapperStatusDot.Fill = active
                        ? Application.Current.Resources["AccentColor"] as System.Windows.Media.SolidColorBrush
                        : Application.Current.Resources["TextPrimary"] as System.Windows.Media.SolidColorBrush;
                }
            }
            catch { }

            // MODULES
            try
            {
                var modules = InterceptionManager.Modules.Where(x => x.Togglable);
                var pairs = modules.Select(x => (module: x, display: DisplayModules.FirstOrDefault(y => y.ModuleName == x.Name))).ToArray();

                foreach (var p in pairs)
                {
                    var display = p.display;
                    if (p.module.IsActivated && display is null)
                    {   // New active found
                        var d = new EnabledModuleTimer(p.module.Name) { Visibility = Visibility.Collapsed };
                        DisplayModules.Add(d);
                        Dispatcher.BeginInvoke(() => d.ElementAppear()); 
                    }

                    if (display is not null)
                    {
                        display.UpdateTimer();

                        if (!p.module.IsActivated && DateTime.Now - p.module.StartTime > TimeSpan.FromSeconds(Config.Instance.Settings.Window_TimerDecaySeconds))
                        {
                            Dispatcher.BeginInvoke(async () =>
                            {
                                display.ElementDisappear();
                                await Task.Delay(animationTime);
                                DisplayModules.Remove(display);
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Poll modules: {ex}");
            }

            // OTHER FUNCS
            try
            {
                CurrentTime.Content = DateTime.Now.ToString("HH:mm:ss");
                OverlayWindow.CheckGameFocus();
            }
            catch (Exception ex)
            {
                Logger.Error($"Main poll: {ex}");
            }
        }

        private static string ModulePortName(string name) => name switch
        {
            "PVE"       => "3074",
            "PVP"       => "27K",
            "З0K"       => "30K",
            "API Block" => "7500",
            "Multishot" => "SHOT",
            "Swapper"   => "SWAP",
            _           => name
        };

        private void ModuleTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                var moduleName = btn.Tag?.ToString();
                if (moduleName != null && InterceptionManager.GetModule(moduleName) != null)
                {
                    Config.Instance.CurrentModule = moduleName;
                    UpdateSelectedModule();
                }
            }
        }

        private void UpdateTabHighlights()
        {
            var map = new (string tabName, string module)[]
            {
                ("Tab_3074", "PVE"), ("Tab_27K", "PVP"), ("Tab_30K", "З0K"),
                ("Tab_7500", "API Block"), ("Tab_SWAP", "Swapper"), ("Tab_SHOT", "Multishot")
            };
            var activeTxt = Application.Current.FindResource("TextPrimary") as SolidColorBrush;
            var inactive  = Application.Current.FindResource("TextSecondary") as SolidColorBrush;
            foreach (var (tabName, module) in map)
            {
                if (FindName(tabName) is System.Windows.Controls.Button btn)
                {
                    bool active = CurrentModuleName == module;
                    btn.Foreground = active ? activeTxt : inactive;
                    if (btn.Template.FindName("UnderLine", btn) is Border line)
                        line.Opacity = active ? 1.0 : 0.0;
                }
            }
        }

        private void UpdateSelectedModule()
        {
            var targetModule = CurrentModuleName;
            Logger.Info($"UpdateSelectedModule called - CurrentModuleName: {targetModule}");

            if (CurrentModule is null)
            {
                Config.Instance.CurrentModule = targetModule = InterceptionManager.Modules.First().Name;
                Logger.Info($"CurrentModule was null, set to first module: {targetModule}");
            }

            SelectedModuleLabel.Content = ModulePortName(targetModule);
            SelectedModuleButton.PathData = CurrentModule.Icon;
            SelectedModuleButton.GlowColor = CurrentModule.Color;
            SelectedModuleButton.RefreshAppearance(null, null);

            Description.Text = CurrentModule.Description;

            KeybindButton.Text = Keybind.Any()
                ? String.Join(" + ", Keybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";
            ModuleCheckbox.SetState(Config.GetNamed(targetModule).Enabled);

            if (CurrentModule is PveModule)
            {
                PveInCB.SetState(PveModule.Inbound);
                PveOutCB.SetState(PveModule.Outbound);
                PveSlowInCB.SetState(PveModule.SlowInbound);
                PveSlowOutCB.SetState(PveModule.SlowOutbound);

                PveResyncCB.SetState(PveModule.AutoResync);
                PveBufferCB.SetState(PveModule.Buffer);

                PveInbound.Text = Config.GetNamed("PVE").Keybind.Any()
                    ? String.Join(" + ", Config.GetNamed("PVE").Keybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                PveOutbound.Text = PveModule.OutboundKeybind.Any()
                    ? String.Join(" + ", PveModule.OutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                PveSlowInbound.Text = PveModule.SlowInboundKeybind.Any()
                    ? String.Join(" + ", PveModule.SlowInboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PveSlowOutbound.Text = PveModule.SlowOutboundKeybind.Any()
                    ? String.Join(" + ", PveModule.SlowOutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                var motesBind = Config.Instance.Settings.ZaqlMotes_Keybind;
                MotesKeybindBtn.Text = motesBind.Any()
                    ? String.Join(" + ", motesBind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PVE_Panel.Visibility = PveInCB.Visibility = Visibility.Visible;
                ActivationGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                kbd.Content = "Keybind";
                ActivationGrid.ToolTip = null;
                ActivationGrid.Visibility = Visibility.Visible;
                PVE_Panel.Visibility = PveInCB.Visibility = Visibility.Collapsed;
            }



            if (CurrentModule is PvpModule)
            {
                PvpInCB.SetState(PvpModule.Inbound);
                PvpOutCB.SetState(PvpModule.Outbound);
                PvpResyncCB.SetState(PvpModule.AutoResync);
                PvpBufferCB.SetState(PvpModule.Buffer);

                PvpOutbound.Text = PvpModule.OutboundKeybind.Any()
                    ? String.Join(" + ", PvpModule.OutboundKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                PVP_Panel.Visibility = PvpInCB.Visibility = Visibility.Visible;
            }
            else
            {
                kbd.Content = "Keybind";
                ActivationGrid.ToolTip = null;
                PVP_Panel.Visibility = PvpInCB.Visibility = Visibility.Collapsed;
            }


            if (CurrentModule is PvpModule)
            {
                kbd.Content = "DL";
                ActivationGrid.ToolTip = "Block info sent by players";
            }
            if (CurrentModule is PveModule)
            {
                kbd.Content = "DL";
                ActivationGrid.ToolTip = "Block info sent by server";
            }


            if (CurrentModule is MultishotModule ms)
            {
                MS_MaxTimeSlider.Value = MultishotModule.MaxTime;
                MS_DETECT.SetState(MultishotModule.WaitShot);
                MS_PVP.SetState(MultishotModule.PlayersMode);
                MS_INBOUND.SetState(MultishotModule.Inbound);
                MS_OUTBOUND.SetState(MultishotModule.Outbound);
                MS_TOGGLABLE.SetState(ms.Togglable);

                MS_PVPKeybind.Text = MultishotModule.PlayersKeybind.Any()
                    ? String.Join(" + ", MultishotModule.PlayersKeybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";

                MULTISHOT_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                MULTISHOT_Panel.Visibility = Visibility.Collapsed;
            }



            if (CurrentModule is ApiModule)
            {
                API_Disable.SetState(ApiModule.Disable);
                API_Buffer.SetState(ApiModule.Buffer);
                API_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                API_Panel.Visibility = Visibility.Collapsed;
            }

            if (CurrentModule is InstanceModule)
            {
                InstBufferCB.SetState(InstanceModule.Buffer);
                InstSlowCB.SetState(InstanceModule.RateLimitingEnabled);
                InstRateLimitTB.Text = InstanceModule.TargetBytesPerSecond.ToString();
                ReconnectHoldTB.Text = ReconnectModule.HoldSeconds.ToString();
                var rcBind = Config.GetNamed("Reconnect").Keybind;
                ReconnectKeybindBtn.Text = rcBind.Any()
                    ? string.Join(" + ", rcBind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
                INSTANCE_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                INSTANCE_Panel.Visibility = Visibility.Collapsed;
            }

            if (CurrentModule is SwapperModule)
            {
                SWAPPER_Panel.Visibility = Visibility.Visible;
                LoadSwapProfileToUI(_currentSwapProfile);
            }
            else
            { API_Panel.Visibility = Visibility.Collapsed;
                SWAPPER_Panel.Visibility = Visibility.Collapsed;
            }

            UpdateTabHighlights();
        }


        private void MotesBtnClick(object sender, RoutedEventArgs e)
        {
            Task.Run(Interception.MotesModule.Run);
        }

        // Keybind logic
        private Dictionary<Controls.Button, List<Keycode>> listening = new ();
        private DateTime lastUpdated = DateTime.MinValue;
        private SemaphoreSlim keybindSemaphore = new(1);
        private void KeybindButtonClick(object sender, RoutedEventArgs e)
        {
            if (DateTime.Now - lastUpdated > TimeSpan.FromSeconds(0.15) && keybindSemaphore.CurrentCount > 0)
            {
                var button = sender as Controls.Button;
                keybindSemaphore.Wait();
                bool listen = !listening.ContainsKey(button);

                if (listen)
                {
                    if (button == KeybindButton)
                        listening.Add(button, Keybind);
                    else if (button == PveInbound)
                        listening.Add(button, Config.GetNamed("PVE").Keybind);
                    else if (button == PveOutbound)
                        listening.Add(button, PveModule.OutboundKeybind);
                    else if (button == PveSlowInbound)
                        listening.Add(button, PveModule.SlowInboundKeybind);
                    else if (button == MS_PVPKeybind)
                        listening.Add(button, MultishotModule.PlayersKeybind);
                    else if (button == PvpOutbound)
                        listening.Add(button, PvpModule.OutboundKeybind);
                    else if (button == PveSlowOutbound)
                        listening.Add(button, PveModule.SlowOutboundKeybind);
                    else if (button == SwapperKeybindBtn)
                        listening.Add(button, SwapperModule.Profiles[_currentSwapProfile].Keybind);
                    else if (button == ReconnectKeybindBtn)
                        listening.Add(button, Config.GetNamed("Reconnect").Keybind);
                    else if (button == MotesKeybindBtn)
                        listening.Add(button, Config.Instance.Settings.ZaqlMotes_Keybind);
                    
                    //button.Background = new SolidColorBrush(Color.FromArgb(0x88, 0xD9, 0xCC, 0xD9));
                    if (listening.Count == 1)
                    {
                        InterceptionManager.Modules.ForEach(x => x.UnhookKeybind());
                        KeyListener.KeysPressed += ListeningNewKeybind;
                        SwapperModule.IsCapturingKeybind = true;
                    }
                    button.ButtonBorder.BorderThickness = new Thickness(1.75);
                    button.ButtonBorder.BorderBrush = Brushes.White;
                    button.ButtonBorder.Effect = new DropShadowEffect()
                    {
                        ShadowDepth = 0,
                        Color = Colors.White,
                        BlurRadius = 8
                    };
                }
                else
                {
                    listening.Remove(button);
                    //button.Background = Application.Current.Resources["InactiveColor"] as SolidColorBrush;
                    if (listening.Count == 0)
                    {
                        InterceptionManager.Modules.ForEach(x => x.HookKeybind());
                        KeyListener.KeysPressed -= ListeningNewKeybind;
                        SwapperModule.IsCapturingKeybind = false;
                    }
                    button.ButtonBorder.BorderThickness = new Thickness(0);
                    button.ButtonBorder.BorderBrush = Brushes.Transparent;
                    button.ButtonBorder.Effect = null;

                    Config.Save();
                }

                keybindSemaphore.Release();
                lastUpdated = DateTime.Now;
            }

            void ListeningNewKeybind(LinkedList<Keycode> keycodes)
            {
                if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_LMB)
                    return;

                foreach (var b in listening.Values)
                    b.Clear();

                if (keycodes.Count == 1 && keycodes.First.Value == Keycode.VK_ESC)
                {
                    Dispatcher.Invoke(DispatcherPriority.Background, () =>
                    {
                        foreach (var b in listening.Keys)
                            b.Text = "No keybind";
                    });
                    return;
                }

                foreach (var b in listening.Values)
                    b.AddRange(keycodes);

                Config.Save();

                Dispatcher.Invoke(DispatcherPriority.Background, () =>
                {
                    try
                    {
                        foreach (var b in listening)
                            b.Key.Text = String.Join(" + ", b.Value.Select(x => x.ToString().Replace("VK_", "")));
                    }
                    catch {}
                });
            }
        }
        private void LoadSwapProfileToUI(int idx)
        {
            _suppressSwapperUI = true;
            try
            {
                var p = SwapperModule.Profiles[idx];
                SwapperProfileCounter.Text = $"{idx + 1} / 5";
                SwapperProfileName.Text = p.Name;
                for (int i = 1; i <= 20; i++)
                {
                    var cb = FindName($"Swapper_Slot{i}") as Controls.Checkbox;
                    cb?.SetState(p.LoadoutEnabled[i - 1]);
                }
                SwapperFinalSlot.Text = p.EndingLoadout.ToString();
                SwapperSwapDuration.Text = p.SwapDuration.ToString();
                SwapperDelayBetween.Text = p.SwapDelay.ToString();
                SwapperUntickDelayTB.Text = p.UntickDelay.ToString();
                SwapperPort3074UL.SetState(p.Port3074);
                SwapperPort27kUL.SetState(p.Port27k);
                SwapperPort3074DL.SetState(p.Packet3074DL);
                SwapperDisableBuffering.SetState(p.AutoDisableBuffering);
                SwapperCloseInventoryCB.SetState(p.CloseInventory);
                SwapperTickFullGame.SetState(p.FullGame);
                SwapperOpenInventoryCB.SetState(p.OpenInventory);
                SwapperKeybindBtn.Text = p.Keybind.Any()
                    ? string.Join(" + ", p.Keybind.Select(x => x.ToString().Replace("VK_", "")))
                    : "No keybind";
            }
            finally { _suppressSwapperUI = false; }
        }

        private void SwapperProfilePrev_Click(object sender, RoutedEventArgs e)
        {
            _currentSwapProfile = (_currentSwapProfile - 1 + 5) % 5;
            SwapperModule.CurrentProfileIndex = _currentSwapProfile;
            LoadSwapProfileToUI(_currentSwapProfile);
        }

        private void SwapperProfileNext_Click(object sender, RoutedEventArgs e)
        {
            _currentSwapProfile = (_currentSwapProfile + 1) % 5;
            SwapperModule.CurrentProfileIndex = _currentSwapProfile;
            LoadSwapProfileToUI(_currentSwapProfile);
        }

        private void SwapperProfileName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSwapperUI || !(sender is TextBox tb)) return;
            SwapperModule.Profiles[_currentSwapProfile].Name = tb.Text;
        }

        private void Swapper_SlotClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Controls.Checkbox cb)) return;
            if (int.TryParse(cb.Name.Replace("Swapper_Slot", ""), out int n) && n >= 1 && n <= 20)
                SwapperModule.Profiles[_currentSwapProfile].LoadoutEnabled[n - 1] = cb.Checked;
        }

        private void SwapperFinalSlot_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSwapperUI || !(sender is TextBox tb)) return;
            if (int.TryParse(tb.Text, out int v) && v >= 1 && v <= 20)
                SwapperModule.Profiles[_currentSwapProfile].EndingLoadout = v;
        }

        private void SwapperSwapDuration_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSwapperUI || !(sender is TextBox tb)) return;
            if (int.TryParse(tb.Text, out int v) && v > 0)
                SwapperModule.Profiles[_currentSwapProfile].SwapDuration = v;
        }

        private void SwapperDelayBetween_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSwapperUI || !(sender is TextBox tb)) return;
            if (int.TryParse(tb.Text, out int v) && v >= 0)
                SwapperModule.Profiles[_currentSwapProfile].SwapDelay = v;
        }

        private void SwapperUntickDelayTB_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSwapperUI || !(sender is TextBox tb)) return;
            if (int.TryParse(tb.Text, out int v) && v >= 0)
                SwapperModule.Profiles[_currentSwapProfile].UntickDelay = v;
        }

        private void SwapperPort3074UL_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].Port3074 = cb.Checked;
        }

        private void SwapperPort27kUL_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].Port27k = cb.Checked;
        }

        private void SwapperPort3074DL_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].Packet3074DL = cb.Checked;
        }

        private void SwapperDisableBuffering_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].AutoDisableBuffering = cb.Checked;
        }

        private void SwapperCloseInventoryCB_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].CloseInventory = cb.Checked;
        }

        private void SwapperTickFullGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].FullGame = cb.Checked;
        }

        private void SwapperOpenInventoryCB_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox cb)
                SwapperModule.Profiles[_currentSwapProfile].OpenInventory = cb.Checked;
        }

        private void SwapperSaveAll_Click(object sender, RoutedEventArgs e)
        {
            Config.Save();
        }
    }
}  