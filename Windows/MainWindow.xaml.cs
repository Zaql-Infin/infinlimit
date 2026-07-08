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
        private int SwapperFinalLoadoutNumber => Config.GetNamed("Swapper").GetSettings<int>("FinalLoadoutNumber");


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

            try
            {
                for (int i = 1; i <= 12; i++)
                {
                    var cb = FindName($"Swapper_Loadout{i}") as InfinLimit.Controls.Checkbox;
                    if (cb != null)
                    {
                        cb.Click += Swapper_Loadout_CheckedChanged;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting up loadout checkboxes: {ex}");
            }
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

        private void UpdateSelectedModule()
        {
            var targetModule = CurrentModuleName;
            Logger.Info($"UpdateSelectedModule called - CurrentModuleName: {targetModule}");

            if (CurrentModule is null)
            {
                Config.Instance.CurrentModule = targetModule = InterceptionManager.Modules.First().Name;
                Logger.Info($"CurrentModule was null, set to first module: {targetModule}");
            }

            SelectedModuleLabel.Content = targetModule;
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

                PVE_Panel.Visibility = PveInCB.Visibility = Visibility.Visible;
            }
            else
            {
                kbd.Content = "Keybind";
                ActivationGrid.ToolTip = null;
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
                InstRateLimitTB.Text = InstanceModule.TargetBitsPerSecond.ToString();
                INSTANCE_Panel.Visibility = Visibility.Visible;
            }
            else
            {
                INSTANCE_Panel.Visibility = Visibility.Collapsed;
            }

            if (CurrentModule is SwapperModule)
            {
                Logger.Info("CurrentModule is SwapperModule - setting SWAPPER_Panel to visible");
                SWAPPER_Panel.Visibility = Visibility.Visible;

                var swapperSettings = Config.GetNamed("Swapper");
                var selected = swapperSettings.GetSettings<List<int>>("SelectedLoadouts") ?? new List<int>();
                var endOn = swapperSettings.GetSettings<int>("EndOnLoadout");

                Logger.Info($"Swapper settings loaded - Selected loadouts: {string.Join(", ", selected)}, End on: {endOn}");

                // Restore loadout selection states with detailed logging
                Logger.Info($"Swapper: Starting to restore {selected.Count} selected loadouts: [{string.Join(", ", selected)}]");
                
                for (int i = 1; i <= 12; i++)
                {
                    var cb = FindName($"Swapper_Loadout{i}") as InfinLimit.Controls.Checkbox;
                    if (cb != null)
                    {
                        bool shouldBeSelected = selected.Contains(i);
                        bool wasCheckedBefore = cb.Checked;
                        
                        cb.SetState(shouldBeSelected);
                        
                        bool isCheckedAfter = cb.Checked;
                        Logger.Info($"Swapper: Loadout {i} - WasBefore: {wasCheckedBefore}, ShouldBe: {shouldBeSelected}, IsAfter: {isCheckedAfter}");
                        
                        // Double-check by forcing the state again if it didn't stick
                        if (cb.Checked != shouldBeSelected)
                        {
                            Logger.Warning($"Swapper: State didn't stick for loadout {i}, forcing again");
                            cb.SetState(shouldBeSelected);
                        }
                    }
                    else
                    {
                        Logger.Error($"Swapper: Checkbox Swapper_Loadout{i} not found!");
                    }
                }
                
                Logger.Info($"Swapper: Restored {selected.Count} selected loadouts to UI");
                
                // Update final loadout number
                var finalLoadoutNum = swapperSettings.GetSettings<int>("FinalLoadoutNumber");
                if (finalLoadoutNum == 0) finalLoadoutNum = 1; // Default to 1
                SwapperFinalLoadout.Text = finalLoadoutNum.ToString();
                
                // Update keybind button texts
                UpdateSwapperKeybindButtonTexts();
                
                // Update duration
                var duration = swapperSettings.GetSettings<int>("LoopDuration");
                if (duration == 0) duration = 5000; // Default to 5 seconds
                SwapperDuration.Text = duration.ToString();
            }
            else
            { API_Panel.Visibility = Visibility.Collapsed;
                SWAPPER_Panel.Visibility = Visibility.Collapsed;
            }
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
                    else if (button == SwapperModule3074UL)
                        listening.Add(button, SwapperModule.Module3074ULKeybind);
                    else if (button == SwapperModule3074DL)
                        listening.Add(button, SwapperModule.Module3074DLKeybind);
                    else if (button == SwapperModule27kUL)
                        listening.Add(button, SwapperModule.Module27kULKeybind);
                    
                    //button.Background = new SolidColorBrush(Color.FromArgb(0x88, 0xD9, 0xCC, 0xD9));
                    if (listening.Count == 1)
                    {
                        InterceptionManager.Modules.ForEach(x => x.UnhookKeybind());
                        KeyListener.KeysPressed += ListeningNewKeybind;
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
        // Handler for checkbox changes
        private void Swapper_Loadout_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var cb = sender as InfinLimit.Controls.Checkbox;
            if (cb != null)
            {
                cb.Background = cb.Checked
                    ? (SolidColorBrush)Application.Current.Resources["AccentColor"]
                    : (SolidColorBrush)Application.Current.Resources["InactiveColor"];
            }

            var swapper = InterceptionManager.GetModule("Swapper") as SwapperModule;
            if (swapper != null)
            {
                for (int i = 1; i <= 12; i++)
                {
                    var checkbox = FindName($"Swapper_Loadout{i}") as InfinLimit.Controls.Checkbox;
                    bool isSelected = checkbox != null && checkbox.Checked;
                    swapper.SetLoadoutSelection(i, isSelected);
                }
            }
        }

        // Handler for loadout checkbox clicks
        private void Swapper_LoadoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.Checkbox checkbox)
            {
                // Extract loadout number from checkbox name (e.g., "Swapper_Loadout3" -> 3)
                var checkboxName = checkbox.Name;
                if (checkboxName.StartsWith("Swapper_Loadout") && 
                    int.TryParse(checkboxName.Replace("Swapper_Loadout", ""), out int loadoutNumber))
                {
                    var swapperSettings = Config.GetNamed("Swapper");
                    var selectedLoadouts = swapperSettings.GetSettings<List<int>>("SelectedLoadouts") ?? new List<int>();
                    
                    if (checkbox.Checked)
                    {
                        // Add loadout to selection if not already present
                        if (!selectedLoadouts.Contains(loadoutNumber))
                        {
                            selectedLoadouts.Add(loadoutNumber);
                            Logger.Info($"Swapper: Added loadout {loadoutNumber} to selection");
                        }
                    }
                    else
                    {
                        // Remove loadout from selection
                        if (selectedLoadouts.Contains(loadoutNumber))
                        {
                            selectedLoadouts.Remove(loadoutNumber);
                            Logger.Info($"Swapper: Removed loadout {loadoutNumber} from selection");
                        }
                    }
                    
                    // Save the updated list to settings
                    swapperSettings.Settings["SelectedLoadouts"] = selectedLoadouts;
                    
                    // Update the SwapperModule instance if it exists
                    var swapperModule = InterceptionManager.GetModule("Swapper") as SwapperModule;
                    swapperModule?.SetLoadoutSelection(loadoutNumber, checkbox.Checked);
                    
                    Config.Save();
                    
                    Logger.Info($"Swapper: Current selected loadouts: {string.Join(", ", selectedLoadouts.OrderBy(x => x))}");
                }
            }
        }
        
        // Handler for final loadout number text change
        private void SwapperFinalLoadout_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Validate input is a number between 1-12
                if (int.TryParse(textBox.Text, out int loadoutNumber) && loadoutNumber >= 1 && loadoutNumber <= 12)
                {
                    var swapperSettings = Config.GetNamed("Swapper");
                    swapperSettings.Settings["FinalLoadoutNumber"] = loadoutNumber;
                    Config.Save();
                    
                    // Update the SwapperModule with the new final loadout number
                    var swapperModule = InterceptionManager.GetModule("Swapper") as SwapperModule;
                    swapperModule?.SetFinalLoadout(loadoutNumber);
                    
                    Logger.Info($"Swapper: Final loadout set to {loadoutNumber}");
                }
                else if (!string.IsNullOrEmpty(textBox.Text))
                {
                    // Invalid input, revert to previous valid value
                    var swapperSettings = Config.GetNamed("Swapper");
                    var currentValue = swapperSettings.GetSettings<int>("FinalLoadoutNumber");
                    if (currentValue == 0) currentValue = 1;
                    textBox.Text = currentValue.ToString();
                    textBox.SelectionStart = textBox.Text.Length; // Move cursor to end
                }
            }
        }
        
        // Helper method to update keybind button texts
        private void UpdateSwapperKeybindButtonTexts()
        {
            // Update 3074 UL button text
            SwapperModule3074UL.Text = SwapperModule.Module3074ULKeybind.Any()
                ? String.Join(" + ", SwapperModule.Module3074ULKeybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";
            
            // Update 3074 DL button text
            SwapperModule3074DL.Text = SwapperModule.Module3074DLKeybind.Any()
                ? String.Join(" + ", SwapperModule.Module3074DLKeybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";
            
            // Update 27k UL button text
            SwapperModule27kUL.Text = SwapperModule.Module27kULKeybind.Any()
                ? String.Join(" + ", SwapperModule.Module27kULKeybind.Select(x => x.ToString().Replace("VK_", "")))
                : "No keybind";
        }
        
        // Handler for duration text change
        private void SwapperDuration_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Validate input is a positive number
                if (int.TryParse(textBox.Text, out int duration) && duration > 0)
                {
                    var swapperSettings = Config.GetNamed("Swapper");
                    swapperSettings.Settings["LoopDuration"] = duration;
                    Config.Save();
                    
                    // Update the SwapperModule with the new duration
                    var swapperModule = InterceptionManager.GetModule("Swapper") as SwapperModule;
                    swapperModule?.SetLoopDuration(duration);
                    
                    Logger.Info($"Swapper: Loop duration set to {duration}ms");
                }
                else if (!string.IsNullOrEmpty(textBox.Text))
                {
                    // Invalid input, revert to previous valid value
                    var swapperSettings = Config.GetNamed("Swapper");
                    var currentValue = swapperSettings.GetSettings<int>("LoopDuration");
                    if (currentValue == 0) currentValue = 5000; // Default to 5 seconds
                    textBox.Text = currentValue.ToString();
                    textBox.SelectionStart = textBox.Text.Length; // Move cursor to end
                }
            }
        }
    }   
}  