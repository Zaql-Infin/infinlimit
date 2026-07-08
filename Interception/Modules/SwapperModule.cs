using InfinLimit.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;

namespace InfinLimit.Interception.Modules
{
    public class SwapperModule : PacketModuleBase
    {
        #region Win32 API Imports for Timing and Input
        
        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceCounter(out long lpPerformanceCount);
        
        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceFrequency(out long lpFrequency);
        
        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);
        
        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
        
        [DllImport("gdi32.dll")]
        static extern uint GetPixel(IntPtr hDC, int x, int y);
        
        // Key event flags
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        
        // Mouse event flags
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        
        #endregion
        
        #region Resolution and Coordinate Management
        
        // Coordinates for each loadout based on screen resolution
        private Dictionary<string, (int x, int y)> coordinates = new();
        
        // Special coordinates for UI elements
        private (int x, int y) loadColorCoord;
        private (int x, int y) invColorCoord;
        private (int x, int y) invColor2Coord;
        private (int x, int y) loadDualCoord; // Damage loadout coordinate
        
        // Screen resolution info
        private int screenWidth;
        private int screenHeight;
        private double hwMultiplier;
        private double wOffset;
        
        #endregion
        
        #region Settings and Configuration
        
        // Loadout selection settings  
        private int damageLoadout = 1;
        private int finalLoadoutNumber = 1; // The loadout number to end the loop on
        private string activateModule = "None"; // Module to activate during swapping
        private int loopDuration = 5000; // Duration to loop in milliseconds
        
        // Module activation settings (replacing keybind strings)
        private bool use3074Upload = true;    // PVE Upload (3074 UL)
        private bool use3074Download = false;  // PVE Download (3074 DL) 
        private bool use27kUpload = false;     // PVP Upload (27k UL)
        private bool f1AfterSwaps = false;
        
        // Timing settings
        private int swapTime = 5000;           // Total swap time in ms
        private int timeBetweenLoadouts = 30;  // Delay between loadout clicks in ms
        
        
        // Keybinds for each module type
        public static List<Keycode> Module3074ULKeybind = new List<Keycode>();
        public static List<Keycode> Module3074DLKeybind = new List<Keycode>();
        public static List<Keycode> Module27kULKeybind = new List<Keycode>();
        
        #endregion

        public SwapperModule() : base("Swapper", true)
        {
            Icon = System.Windows.Application.Current.FindResource("SwapperIcon") as Geometry;
            Description = "Advanced loadout swapper with pixel detection, precise timing, and module integration (PVE/PVP activation).";

            InitializeResolutionAndCoordinates();
            LoadSettings();
            
            // Initialize damage loadout coordinate
            UpdateDamageLoadoutCoordinate(damageLoadout);
            
            // Set up keybind handlers
            KeyListener.KeysPressed += Module3074ULKeybindHandler;
            KeyListener.KeysPressed += Module3074DLKeybindHandler;
            KeyListener.KeysPressed += Module27kULKeybindHandler;
        }
        
        private void LoadSettings()
        {
            var config = Config.GetNamed("Swapper");
            
            // Load other settings with defaults
            damageLoadout = config.GetSettings<int>("DamageLoadout") > 0 ? config.GetSettings<int>("DamageLoadout") : 1;
            finalLoadoutNumber = config.GetSettings<int>("FinalLoadoutNumber") > 0 ? config.GetSettings<int>("FinalLoadoutNumber") : 1;
            loopDuration = config.GetSettings<int>("LoopDuration") > 0 ? config.GetSettings<int>("LoopDuration") : 5000;
            
            // Load keybinds for each module type
            Module3074ULKeybind.Clear();
            Module3074DLKeybind.Clear();
            Module27kULKeybind.Clear();
            
            Module3074ULKeybind.AddRange(config.GetSettings<List<Keycode>>("Module3074ULKeybind") ?? new List<Keycode>());
            Module3074DLKeybind.AddRange(config.GetSettings<List<Keycode>>("Module3074DLKeybind") ?? new List<Keycode>());
            Module27kULKeybind.AddRange(config.GetSettings<List<Keycode>>("Module27kULKeybind") ?? new List<Keycode>());
            
            // Module activation settings (replacing keybind strings)
            use3074Upload = config.GetSettings<bool>("Use3074Upload");      // Default: true from GetSettings
            use3074Download = config.GetSettings<bool>("Use3074Download");    // Default: false 
            use27kUpload = config.GetSettings<bool>("Use27kUpload");         // Default: false
            f1AfterSwaps = config.GetSettings<bool>("F1AfterSwaps");        // Default: false
            
            // Determine activateModule based on boolean settings (for backwards compatibility)
            activateModule = config.GetSettings<string>("ActivateModule");
            if (string.IsNullOrEmpty(activateModule))
            {
                // If no explicit ActivateModule setting, determine from boolean settings
                if (use3074Upload)
                    activateModule = "3074 UL";
                else if (use3074Download)
                    activateModule = "3074 DL";
                else if (use27kUpload)
                    activateModule = "27k UL";
                else
                    activateModule = "None";
            }
            
            // Timing settings
            swapTime = config.GetSettings<int>("SwapTimeOverall") > 0 ? config.GetSettings<int>("SwapTimeOverall") : 5000;
            timeBetweenLoadouts = config.GetSettings<int>("DelayBetweenLoadouts") > 0 ? config.GetSettings<int>("DelayBetweenLoadouts") : 30;
            
            // Load selected loadouts from config
            var selectedLoadoutsList = GetSelectedLoadouts();
            Logger.Info($"Swapper: Loaded settings - Selected Loadouts: [{string.Join(", ", selectedLoadoutsList.OrderBy(x => x))}], Damage Loadout: {damageLoadout}, Final Loadout: {finalLoadoutNumber}, Activate Module: {activateModule}, Duration: {loopDuration}ms, 3074UL: {use3074Upload}, 3074DL: {use3074Download}, 27kUL: {use27kUpload}");
        }
        
        #region Resolution and Coordinate Management Methods
        

        /// Initializes screen resolution detection and calculates coordinates based on resolution

        private void InitializeResolutionAndCoordinates()
        {
            // Get primary screen resolution
            screenWidth = Screen.PrimaryScreen.Bounds.Width;
            screenHeight = Screen.PrimaryScreen.Bounds.Height;
            
            Logger.Info($"Swapper: Detected screen resolution: {screenWidth}x{screenHeight}");
            
            // Calculate coordinates based on resolution
            if (screenWidth == 1920 && screenHeight == 1080)
            {
                Set1920x1080Coordinates();
                Logger.Info("Swapper: Using 1920x1080 coordinate set");
            }
            else if (screenWidth == 2560 && screenHeight == 1440)
            {
                Set2560x1440Coordinates();
                Logger.Info("Swapper: Using 2560x1440 coordinate set");
            }
            else
            {
                SetScaledCoordinates();
                Logger.Info($"Swapper: Using scaled coordinates (unsupported resolution: {screenWidth}x{screenHeight})");
            }
        }
        

        /// Sets coordinates for 1920x1080 resolution (from AHK script)

        private void Set1920x1080Coordinates()
        {
            coordinates["load1"] = (140, 340);
            coordinates["load2"] = (240, 340);
            coordinates["load3"] = (140, 440);
            coordinates["load4"] = (240, 440);
            coordinates["load5"] = (140, 530);
            coordinates["load6"] = (240, 530);
            coordinates["load7"] = (140, 630);
            coordinates["load8"] = (240, 630);
            coordinates["load9"] = (140, 730);
            coordinates["load10"] = (240, 730);
            coordinates["load11"] = (140, 830);
            coordinates["load12"] = (240, 830);
            
            loadColorCoord = (107, 1044);
            invColorCoord = (960, 1035);
            invColor2Coord = (960, 1014);
        }
        

        /// Sets coordinates for 2560x1440 resolution (from AHK script)

        private void Set2560x1440Coordinates()
        {
            coordinates["load1"] = (190, 450);
            coordinates["load2"] = (320, 450);
            coordinates["load3"] = (190, 570);
            coordinates["load4"] = (320, 570);
            coordinates["load5"] = (190, 690);
            coordinates["load6"] = (320, 690);
            coordinates["load7"] = (190, 810);
            coordinates["load8"] = (320, 810);
            coordinates["load9"] = (190, 950);
            coordinates["load10"] = (320, 950);
            coordinates["load11"] = (190, 1070);
            coordinates["load12"] = (320, 1070);
            
            loadColorCoord = (143, 1386);
            invColorCoord = (1280, 1380);
            invColor2Coord = (1280, 1351);
        }
        

        /// Sets scaled coordinates for unsupported resolutions (from AHK script logic)

        private void SetScaledCoordinates()
        {
            // Calculate scaling factors based on AHK script logic
            hwMultiplier = (double)screenHeight / 1080.0;
            wOffset = (screenWidth - 1920.0 * hwMultiplier) / 2;
            
            Logger.Info($"Swapper: Scaling - hwMultiplier: {hwMultiplier:F3}, wOffset: {wOffset:F1}");
            
            // Scale all loadout coordinates
            coordinates["load1"] = (ScaleX(140), ScaleY(340));
            coordinates["load2"] = (ScaleX(240), ScaleY(340));
            coordinates["load3"] = (ScaleX(140), ScaleY(440));
            coordinates["load4"] = (ScaleX(240), ScaleY(440));
            coordinates["load5"] = (ScaleX(140), ScaleY(530));
            coordinates["load6"] = (ScaleX(240), ScaleY(530));
            coordinates["load7"] = (ScaleX(140), ScaleY(630));
            coordinates["load8"] = (ScaleX(240), ScaleY(630));
            coordinates["load9"] = (ScaleX(140), ScaleY(730));
            coordinates["load10"] = (ScaleX(240), ScaleY(730));
            coordinates["load11"] = (ScaleX(140), ScaleY(830));
            coordinates["load12"] = (ScaleX(240), ScaleY(830));
            
            // Scale UI element coordinates
            loadColorCoord = (ScaleX(107), ScaleY(1044));
            invColorCoord = (ScaleX(960), ScaleY(1035));
            invColor2Coord = (ScaleX(960), ScaleY(1014));
        }
        

        /// Scales X coordinate based on resolution multiplier and offset

        private int ScaleX(int originalX)
        {
            return (int)(originalX * hwMultiplier + wOffset);
        }
        

        /// Scales Y coordinate based on resolution multiplier

        private int ScaleY(int originalY)
        {
            return (int)(originalY * hwMultiplier);
        }
        

        /// Gets the coordinate for a specific loadout number

        public (int x, int y) GetLoadoutCoordinate(int loadoutNumber)
        {
            if (loadoutNumber < 1 || loadoutNumber > 12)
            {
                Logger.Warning($"Swapper: Invalid loadout number {loadoutNumber}, using loadout 1");
                return coordinates["load1"];
            }
            
            return coordinates[$"load{loadoutNumber}"];
        }
        

        /// Updates the damage loadout coordinate when the setting changes

        public void UpdateDamageLoadoutCoordinate(int loadoutNumber)
        {
            if (loadoutNumber >= 1 && loadoutNumber <= 12)
            {
                loadDualCoord = GetLoadoutCoordinate(loadoutNumber);
                Logger.Info($"Swapper: Set damage loadout to {loadoutNumber} at coordinates ({loadDualCoord.x}, {loadDualCoord.y})");
            }
        }
        

        /// Gets all current coordinates for debugging

        public void LogAllCoordinates()
        {
            Logger.Info($"Swapper Coordinates (Resolution: {screenWidth}x{screenHeight}):");
            for (int i = 1; i <= 12; i++)
            {
                var coord = coordinates[$"load{i}"];
                Logger.Info($"  Loadout {i}: ({coord.x}, {coord.y})");
            }
            Logger.Info($"  Load Color: ({loadColorCoord.x}, {loadColorCoord.y})");
            Logger.Info($"  Inv Color: ({invColorCoord.x}, {invColorCoord.y})");
            Logger.Info($"  Inv Color2: ({invColor2Coord.x}, {invColor2Coord.y})");
            Logger.Info($"  Damage Loadout: ({loadDualCoord.x}, {loadDualCoord.y})");
        }
        
        #endregion
        
        #region Precise Timing and Input Simulation
        

        /// High-precision sleep function equivalent to AHK's PreciseSleep
        /// Uses QueryPerformanceCounter for microsecond accuracy

        /// <param name="milliseconds">Time to sleep in milliseconds</param>
        public void PreciseSleep(int milliseconds)
        {
            if (milliseconds <= 0) return;
            
            QueryPerformanceFrequency(out long frequency);
            QueryPerformanceCounter(out long startTime);
            
            long targetTime = startTime + (frequency * milliseconds / 1000);
            
            while (true)
            {
                QueryPerformanceCounter(out long currentTime);
                if (currentTime >= targetTime) 
                    break;
                    
                // If we have more than 1ms remaining, do a regular sleep to avoid 100% CPU
                long remainingTicks = targetTime - currentTime;
                long remainingMs = (remainingTicks * 1000) / frequency;
                
                if (remainingMs > 1)
                {
                    Thread.Sleep(1);
                }
            }
        }
        

        /// Gets the current high-precision timestamp in milliseconds since system start

        public long GetPreciseTimestamp()
        {
            QueryPerformanceCounter(out long counter);
            QueryPerformanceFrequency(out long frequency);
            return (counter * 1000) / frequency;
        }
        

        /// Enhanced mouse move function with instant positioning

        public void MouseMove(int x, int y)
        {
            SetCursorPos(x, y);
            Logger.Debug($"Swapper: Mouse moved to ({x}, {y})");
        }
        

        /// Enhanced mouse click with precise timing

        public void MouseClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Logger.Debug("Swapper: Left mouse click");
        }
        

        /// Enhanced mouse click with custom button support

        public void MouseClick(string button = "left")
        {
            switch (button.ToLower())
            {
                case "left":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
                case "right":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;
            }
            Logger.Debug($"Swapper: {button} mouse click");
        }
        
        
        /// Sends a key press with modifier support (equivalent to AHK SendModified)
        /// Supports combinations like "^c" (Ctrl+C), "+{F1}" (Shift+F1), etc.

        public void SendModifiedKey(string keyString)
        {
            if (string.IsNullOrEmpty(keyString))
            {
                Logger.Warning("Swapper: Empty key string provided to SendModifiedKey");
                return;
            }
            
            // Parse modifiers
            bool ctrl = keyString.Contains("^");
            bool shift = keyString.Contains("+") && !keyString.Contains("{+"); // Avoid treating {+} as shift modifier
            bool alt = keyString.Contains("!");
            bool win = keyString.Contains("#");
            
            // Clean the key string
            string cleanKey = keyString
                .Replace("^", "")
                .Replace("!", "")
                .Replace("#", "")
                .Replace("{+}", "+"); // Restore literal plus sign
                
            // Remove shift modifier only if it's not a literal plus
            if (shift && !keyString.Contains("{+}"))
            {
                cleanKey = cleanKey.Replace("+", "");
            }
            
            // Remove braces if present
            cleanKey = cleanKey.Replace("{", "").Replace("}", "");
            
            Logger.Debug($"Swapper: Sending key '{cleanKey}' with modifiers: Ctrl={ctrl}, Shift={shift}, Alt={alt}, Win={win}");
            
            // Convert to virtual key code
            byte vkCode = GetVirtualKeyCode(cleanKey);
            if (vkCode == 0)
            {
                Logger.Warning($"Swapper: Unknown key '{cleanKey}'");
                return;
            }
            
            // Send modifier keys down
            if (ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero); // VK_CONTROL
            if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero); // VK_SHIFT
            if (alt) keybd_event(0x12, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero); // VK_MENU (Alt)
            if (win) keybd_event(0x5B, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero); // VK_LWIN
            
            // Small delay between modifiers and main key
            PreciseSleep(5);
            
            // Send main key
            keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            
            // Small delay before releasing modifiers
            PreciseSleep(5);
            
            // Release modifier keys in reverse order
            if (win) keybd_event(0x5B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (alt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        

        /// Sends a simple key press without modifiers

        public void SendKey(Keys key)
        {
            byte vkCode = (byte)key;
            keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Logger.Debug($"Swapper: Sent key {key}");
        }
        

        /// Converts a key string to virtual key code

        private byte GetVirtualKeyCode(string keyString)
        {
            if (string.IsNullOrEmpty(keyString)) return 0;
            
            // Handle function keys (F1-F24)
            if (keyString.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(keyString.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
                {
                    return (byte)(0x70 + fNum - 1); // VK_F1 = 0x70
                }
            }
            
            // Handle special keys
            return keyString.ToUpper() switch
            {
                "ESC" or "ESCAPE" => 0x1B, // VK_ESCAPE
                "ENTER" or "RETURN" => 0x0D, // VK_RETURN
                "SPACE" => 0x20, // VK_SPACE
                "TAB" => 0x09, // VK_TAB
                "LEFT" => 0x25, // VK_LEFT
                "UP" => 0x26, // VK_UP
                "RIGHT" => 0x27, // VK_RIGHT
                "DOWN" => 0x28, // VK_DOWN
                "SHIFT" => 0x10, // VK_SHIFT
                "CTRL" or "CONTROL" => 0x11, // VK_CONTROL
                "ALT" => 0x12, // VK_MENU
                "WIN" or "LWIN" => 0x5B, // VK_LWIN
                "BACKSPACE" => 0x08, // VK_BACK
                "DELETE" or "DEL" => 0x2E, // VK_DELETE
                "HOME" => 0x24, // VK_HOME
                "END" => 0x23, // VK_END
                "PAGEUP" or "PGUP" => 0x21, // VK_PRIOR
                "PAGEDOWN" or "PGDN" => 0x22, // VK_NEXT
                "INSERT" or "INS" => 0x2D, // VK_INSERT
                
                // Numbers
                "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33, "4" => 0x34,
                "5" => 0x35, "6" => 0x36, "7" => 0x37, "8" => 0x38, "9" => 0x39,
                
                // Letters (A-Z)
                "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44, "E" => 0x45,
                "F" => 0x46, "G" => 0x47, "H" => 0x48, "I" => 0x49, "J" => 0x4A,
                "K" => 0x4B, "L" => 0x4C, "M" => 0x4D, "N" => 0x4E, "O" => 0x4F,
                "P" => 0x50, "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
                "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58, "Y" => 0x59, "Z" => 0x5A,
                
                _ => 0 // Unknown key
            };
        }
        

        /// Performs multiple rapid mouse clicks (like the AHK script's final sequence)

        public void MultipleClicks(int count, int[] delays)
        {
            for (int i = 0; i < count && i < delays.Length; i++)
            {
                MouseClick();
                if (delays[i] > 0)
                    PreciseSleep(delays[i]);
            }
        }
        

        /// Performs the rapid-fire clicking sequence
        
        public void RapidFireClicks()
        {
            int[] delays = { 60, 50, 40, 30, 20, 10 };
            MultipleClicks(6, delays);
            Logger.Debug("Swapper: Executed rapid-fire click sequence");
        }
        
        /// <summary>
        /// Performs consistent rapid clicking at a specific coordinate for the given duration
        /// </summary>
        /// <param name="x">X coordinate to click</param>
        /// <param name="y">Y coordinate to click</param>
        /// <param name="intervalMs">Interval between clicks in milliseconds</param>
        /// <param name="durationMs">Total duration to click in milliseconds</param>
        public void RapidClickAtPosition(int x, int y, int intervalMs, int durationMs)
        {
            Logger.Info($"Swapper: Starting rapid clicking at ({x}, {y}) every {intervalMs}ms for {durationMs}ms");
            
            long startTime = GetPreciseTimestamp();
            long endTime = startTime + durationMs;
            int clickCount = 0;
            
            // Move to the position once at the beginning
            MouseMove(x, y);
            PreciseSleep(10); // Small delay after mouse movement
            
            while (GetPreciseTimestamp() < endTime && IsActivated)
            {
                long clickStartTime = GetPreciseTimestamp();
                
                // Perform the click
                MouseClick();
                clickCount++;
                
                // Calculate remaining time for this interval
                long clickEndTime = GetPreciseTimestamp();
                long clickDuration = clickEndTime - clickStartTime;
                long remainingInterval = intervalMs - clickDuration;
                
                // Sleep for the remaining interval time (ensuring we don't go negative)
                if (remainingInterval > 0)
                {
                    PreciseSleep((int)remainingInterval);
                }
            }
            
            long totalTime = GetPreciseTimestamp() - startTime;
            Logger.Info($"Swapper: Completed rapid clicking - {clickCount} clicks in {totalTime}ms (avg {(double)totalTime / clickCount:F1}ms per click)");
        }
        
        /// <summary>
        /// Performs alternating clicks through all selected loadouts for the specified duration
        /// </summary>
        /// <param name="loadoutNumbers">List of loadout numbers to alternate through</param>
        /// <param name="intervalMs">Interval between clicks in milliseconds</param>
        /// <param name="durationMs">Total duration to click in milliseconds</param>
        public void AlternateClickThroughLoadouts(List<int> loadoutNumbers, int intervalMs, int durationMs)
        {
            if (!loadoutNumbers.Any())
            {
                Logger.Warning("Swapper: No loadouts provided for alternating clicks");
                return;
            }
            
            Logger.Info($"Swapper: Starting alternating clicks through {loadoutNumbers.Count} loadouts every {intervalMs}ms for {durationMs}ms");
            Logger.Info($"Swapper: Loadout sequence: [{string.Join(", ", loadoutNumbers)}]");
            
            long startTime = GetPreciseTimestamp();
            long endTime = startTime + durationMs;
            int clickCount = 0;
            int currentLoadoutIndex = 0;
            
            // Pre-calculate all coordinates to avoid repeated lookups
            var loadoutCoords = loadoutNumbers.Select(num => new { LoadoutNum = num, Coord = GetLoadoutCoordinate(num) }).ToArray();
            
            while (GetPreciseTimestamp() < endTime && IsActivated)
            {
                long clickStartTime = GetPreciseTimestamp();
                
                // Get the current loadout to click
                var currentLoadout = loadoutCoords[currentLoadoutIndex];
                
                // Move to the loadout position and click
                MouseMove(currentLoadout.Coord.x, currentLoadout.Coord.y);
                PreciseSleep(2); // Very small delay for mouse positioning
                MouseClick();
                clickCount++;
                
                Logger.Debug($"Swapper: Click #{clickCount} at loadout {currentLoadout.LoadoutNum} ({currentLoadout.Coord.x}, {currentLoadout.Coord.y})");
                
                // Move to the next loadout in the cycle
                currentLoadoutIndex = (currentLoadoutIndex + 1) % loadoutNumbers.Count;
                
                // Calculate remaining time for this interval
                long clickEndTime = GetPreciseTimestamp();
                long clickDuration = clickEndTime - clickStartTime;
                long remainingInterval = intervalMs - clickDuration;
                
                // Sleep for the remaining interval time (ensuring we don't go negative)
                if (remainingInterval > 0)
                {
                    PreciseSleep((int)remainingInterval);
                }
            }
            
            long totalTime = GetPreciseTimestamp() - startTime;
            int cyclesCompleted = clickCount / loadoutNumbers.Count;
            int partialCycleClicks = clickCount % loadoutNumbers.Count;
            
            Logger.Info($"Swapper: Completed alternating clicks - {clickCount} total clicks in {totalTime}ms (avg {(double)totalTime / clickCount:F1}ms per click)");
            Logger.Info($"Swapper: Completed {cyclesCompleted} full cycles through all loadouts + {partialCycleClicks} additional clicks");
        }
        
        /// <summary>
        /// Gets the color at the specified screen coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Color value as a 32-bit integer (RGB)</returns>
        public uint GetPixelColor(int x, int y)
        {
            IntPtr hDC = GetDC(IntPtr.Zero); // Get the device context for the entire screen
            if (hDC == IntPtr.Zero)
            {
                Logger.Warning($"Swapper: Failed to get device context for pixel at ({x}, {y})");
                return 0;
            }
            
            try
            {
                uint color = GetPixel(hDC, x, y);
                Logger.Debug($"Swapper: Pixel at ({x}, {y}) = 0x{color:X6}");
                return color;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hDC);
            }
        }
        
        /// <summary>
        /// Checks if the color at specified coordinates matches the expected color within tolerance
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="expectedColor">Expected color value</param>
        /// <param name="tolerance">Color tolerance (0-255)</param>
        /// <returns>True if colors match within tolerance</returns>
        public bool IsColorMatch(int x, int y, uint expectedColor, int tolerance = 10)
        {
            uint actualColor = GetPixelColor(x, y);
            
            // Extract RGB components
            byte actualR = (byte)(actualColor & 0xFF);
            byte actualG = (byte)((actualColor >> 8) & 0xFF);
            byte actualB = (byte)((actualColor >> 16) & 0xFF);
            
            byte expectedR = (byte)(expectedColor & 0xFF);
            byte expectedG = (byte)((expectedColor >> 8) & 0xFF);
            byte expectedB = (byte)((expectedColor >> 16) & 0xFF);
            
            // Check if within tolerance
            bool match = Math.Abs(actualR - expectedR) <= tolerance &&
                        Math.Abs(actualG - expectedG) <= tolerance &&
                        Math.Abs(actualB - expectedB) <= tolerance;
            
            Logger.Debug($"Swapper: Color match at ({x}, {y}): actual=0x{actualColor:X6}, expected=0x{expectedColor:X6}, tolerance={tolerance}, match={match}");
            return match;
        }
        
        /// <summary>
        /// Waits for a specific color to appear at the specified coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="expectedColor">Expected color value</param>
        /// <param name="timeoutMs">Maximum time to wait in milliseconds</param>
        /// <param name="tolerance">Color tolerance (0-255)</param>
        /// <returns>True if color was found within timeout</returns>
        public bool WaitForColor(int x, int y, uint expectedColor, int timeoutMs = 5000, int tolerance = 10)
        {
            long startTime = GetPreciseTimestamp();
            
            while (GetPreciseTimestamp() - startTime < timeoutMs)
            {
                if (IsColorMatch(x, y, expectedColor, tolerance))
                {
                    Logger.Debug($"Swapper: Color found at ({x}, {y}) after {GetPreciseTimestamp() - startTime}ms");
                    return true;
                }
                
                PreciseSleep(50); // Check every 50ms
            }
            
            Logger.Warning($"Swapper: Color 0x{expectedColor:X6} not found at ({x}, {y}) within {timeoutMs}ms timeout");
            return false;
        }
        
        
        
        #endregion

        #region Public Methods for UI Integration
        

        /// Sets the selection state for a specific loadout number
        public void SetLoadoutSelection(int loadoutNumber, bool isSelected)
        {
            if (loadoutNumber < 1 || loadoutNumber > 12)
                return;
                
            var swapperSettings = Config.GetNamed("Swapper");
            var selectedLoadouts = swapperSettings.GetSettings<List<int>>("SelectedLoadouts") ?? new List<int>();
            
            if (isSelected && !selectedLoadouts.Contains(loadoutNumber))
            {
                selectedLoadouts.Add(loadoutNumber);
                Logger.Debug($"SwapperModule: Added loadout {loadoutNumber} to selection");
            }
            else if (!isSelected && selectedLoadouts.Contains(loadoutNumber))
            {
                selectedLoadouts.Remove(loadoutNumber);
                Logger.Debug($"SwapperModule: Removed loadout {loadoutNumber} from selection");
            }
            
            // Save the updated list back to settings
            swapperSettings.Settings["SelectedLoadouts"] = selectedLoadouts;
            Config.Save();
        }
        

        /// Gets the current selected loadouts from config
        public List<int> GetSelectedLoadouts()
        {
            var swapperSettings = Config.GetNamed("Swapper");
            return swapperSettings.GetSettings<List<int>>("SelectedLoadouts") ?? new List<int>();
        }
        
        /// Sets the final loadout number that the loop should end on
        public void SetFinalLoadout(int loadoutNumber)
        {
            if (loadoutNumber >= 1 && loadoutNumber <= 12)
            {
                finalLoadoutNumber = loadoutNumber;
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["FinalLoadoutNumber"] = loadoutNumber;
                Config.Save();
                Logger.Info($"Swapper: Final loadout set to {loadoutNumber}");
            }
            else
            {
                Logger.Warning($"Swapper: Invalid final loadout number {loadoutNumber}. Must be between 1-12.");
            }
        }
        
        /// Gets the current final loadout number
        public int GetFinalLoadout()
        {
            return finalLoadoutNumber;
        }
        
        /// Sets the module to activate during swapping
        public void SetActivateModule(string moduleName)
        {
            if (!string.IsNullOrEmpty(moduleName))
            {
                activateModule = moduleName;
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["ActivateModule"] = moduleName;
                
                // Update the corresponding boolean settings based on the module name
                // Reset all to false first
                swapperConfig.Settings["Use3074Upload"] = false;
                swapperConfig.Settings["Use3074Download"] = false;
                swapperConfig.Settings["Use27kUpload"] = false;
                
                // Set the appropriate boolean based on selection
                switch (moduleName)
                {
                    case "3074 UL":
                        swapperConfig.Settings["Use3074Upload"] = true;
                        use3074Upload = true;
                        use3074Download = false;
                        use27kUpload = false;
                        break;
                    case "3074 DL":
                        swapperConfig.Settings["Use3074Download"] = true;
                        use3074Upload = false;
                        use3074Download = true;
                        use27kUpload = false;
                        break;
                    case "27k UL":
                        swapperConfig.Settings["Use27kUpload"] = true;
                        use3074Upload = false;
                        use3074Download = false;
                        use27kUpload = true;
                        break;
                    case "None":
                    default:
                        // All remain false (already set above)
                        use3074Upload = false;
                        use3074Download = false;
                        use27kUpload = false;
                        break;
                }
                
                Config.Save();
                Logger.Info($"Swapper: Activate module set to {moduleName} (3074UL: {use3074Upload}, 3074DL: {use3074Download}, 27kUL: {use27kUpload})");
            }
        }
        
        /// Gets the current activate module
        public string GetActivateModule()
        {
            return activateModule;
        }
        
        /// Sets the loop duration in milliseconds
        public void SetLoopDuration(int duration)
        {
            if (duration > 0)
            {
                loopDuration = duration;
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["LoopDuration"] = duration;
                Config.Save();
                Logger.Info($"Swapper: Loop duration set to {duration}ms");
            }
            else
            {
                Logger.Warning($"Swapper: Invalid loop duration {duration}. Must be greater than 0.");
            }
        }
        
        /// Gets the current loop duration
        public int GetLoopDuration()
        {
            return loopDuration;
        }
        
        /// Sets the damage loadout number
        public void SetDamageLoadout(int loadoutNumber)
        {
            if (loadoutNumber >= 1 && loadoutNumber <= 12)
            {
                damageLoadout = loadoutNumber;
                UpdateDamageLoadoutCoordinate(loadoutNumber);
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["DamageLoadout"] = loadoutNumber;
                Config.Save();
                Logger.Info($"Swapper: Damage loadout set to {loadoutNumber}");
            }
            else
            {
                Logger.Warning($"Swapper: Invalid damage loadout number {loadoutNumber}. Must be between 1-12.");
            }
        }
        
        /// Gets the current damage loadout number
        public int GetDamageLoadout()
        {
            return damageLoadout;
        }
        
        /// Sets the F1 after swaps setting
        public void SetF1AfterSwaps(bool enabled)
        {
            f1AfterSwaps = enabled;
            var swapperConfig = Config.GetNamed("Swapper");
            swapperConfig.Settings["F1AfterSwaps"] = enabled;
            Config.Save();
            Logger.Info($"Swapper: F1 after swaps set to {enabled}");
        }
        
        /// Gets the F1 after swaps setting
        public bool GetF1AfterSwaps()
        {
            return f1AfterSwaps;
        }
        
        /// Sets the overall swap time in milliseconds
        public void SetSwapTime(int timeMs)
        {
            if (timeMs > 0)
            {
                swapTime = timeMs;
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["SwapTimeOverall"] = timeMs;
                Config.Save();
                Logger.Info($"Swapper: Overall swap time set to {timeMs}ms");
            }
            else
            {
                Logger.Warning($"Swapper: Invalid swap time {timeMs}. Must be greater than 0.");
            }
        }
        
        /// Gets the overall swap time
        public int GetSwapTime()
        {
            return swapTime;
        }
        
        
        
        /// Sets the time between loadouts in milliseconds
        public void SetTimeBetweenLoadouts(int timeMs)
        {
            if (timeMs > 0)
            {
                timeBetweenLoadouts = timeMs;
                var swapperConfig = Config.GetNamed("Swapper");
                swapperConfig.Settings["DelayBetweenLoadouts"] = timeMs;
                Config.Save();
                Logger.Info($"Swapper: Time between loadouts set to {timeMs}ms");
            }
            else
            {
                Logger.Warning($"Swapper: Invalid time between loadouts {timeMs}. Must be greater than 0.");
            }
        }
        
        /// Gets the time between loadouts
        public int GetTimeBetweenLoadouts()
        {
            return timeBetweenLoadouts;
        }
        
        /// Gets the Use3074Upload setting
        public bool GetUse3074Upload()
        {
            return use3074Upload;
        }
        
        /// Gets the Use3074Download setting
        public bool GetUse3074Download()
        {
            return use3074Download;
        }
        
        /// Gets the Use27kUpload setting
        public bool GetUse27kUpload()
        {
            return use27kUpload;
        }
        
        /// Sets the keybind for 3074 UL module
        public void SetModule3074ULKeybind(List<Keycode> keybind)
        {
            Module3074ULKeybind.Clear();
            Module3074ULKeybind.AddRange(keybind);
            var swapperConfig = Config.GetNamed("Swapper");
            swapperConfig.Settings["Module3074ULKeybind"] = keybind;
            Config.Save();
            Logger.Info($"Swapper: 3074 UL keybind set");
        }
        
        /// Sets the keybind for 3074 DL module
        public void SetModule3074DLKeybind(List<Keycode> keybind)
        {
            Module3074DLKeybind.Clear();
            Module3074DLKeybind.AddRange(keybind);
            var swapperConfig = Config.GetNamed("Swapper");
            swapperConfig.Settings["Module3074DLKeybind"] = keybind;
            Config.Save();
            Logger.Info($"Swapper: 3074 DL keybind set");
        }
        
        /// Sets the keybind for 27k UL module
        public void SetModule27kULKeybind(List<Keycode> keybind)
        {
            Module27kULKeybind.Clear();
            Module27kULKeybind.AddRange(keybind);
            var swapperConfig = Config.GetNamed("Swapper");
            swapperConfig.Settings["Module27kULKeybind"] = keybind;
            Config.Save();
            Logger.Info($"Swapper: 27k UL keybind set");
        }
        
        #endregion
        
        public override void Toggle()
        {
            if (IsActivated)
            {
                IsActivated = false;
                Logger.Info("Swapper: Deactivated");
                return;
            }
            
            IsActivated = true;
            Logger.Info("Swapper: Starting loadout swap sequence with module integration");
            
            // Execute the swap sequence asynchronously
            Task.Run(async () =>
            {
                try
                {
                    await ExecuteSwapSequenceWithModules();
                    IsActivated = false;
                }
                catch (Exception ex)
                {
                    IsActivated = false;
                    Logger.Error(ex, additionalInfo: "Swapper execution error");
                }
            });
        }
        

        /// Executes the full loadout swap sequence with proper module integration
        private async Task ExecuteSwapSequenceWithModules()
        {
            Logger.Info("Swapper: Starting enhanced loadout swap sequence with module integration");
            long sequenceStartTime = GetPreciseTimestamp();
            
            var selectedLoadouts = GetSelectedLoadouts();
            if (!selectedLoadouts.Any())
            {
                Logger.Warning("Swapper: No loadouts selected for swapping");
                return;
            }
            
            Logger.Info($"Swapper: Configuration - Selected loadouts: [{string.Join(", ", selectedLoadouts.OrderBy(x => x))}], Final loadout: {finalLoadoutNumber}, Duration: {loopDuration}ms, Interval: {timeBetweenLoadouts}ms, Module: {activateModule}");
            
            // Step 1: Initial module activation based on current settings
            ActivateSelectedModule();
            
            // Step 2: Perform rapid clicking alternating through all selected loadouts
            int totalClicks = 0;
            
            // Perform alternating clicks through all selected loadouts for the total duration
            await Task.Run(() => AlternateClickThroughLoadouts(selectedLoadouts, timeBetweenLoadouts, loopDuration));
            
            // Calculate approximate total clicks for logging
            totalClicks = loopDuration / timeBetweenLoadouts;
            
            // Step 3: Cleanup - deactivate modules and end on final loadout
            await FinalizeSwapSequence();
            
            long totalTime = GetPreciseTimestamp() - sequenceStartTime;
            Logger.Info($"Swapper: Complete sequence finished - ~{totalClicks} total clicks in {totalTime}ms");
        }
        
        /// <summary>
        /// Activates the selected module based on current settings
        /// </summary>
        private void ActivateSelectedModule()
        {
            if (activateModule == "None")
            {
                Logger.Info("Swapper: No module activation selected");
                return;
            }
            
            Logger.Info($"Swapper: Activating module: {activateModule}");
            
            switch (activateModule)
            {
                case "3074 UL": // PVE Upload
                    var pveModuleUL = InterceptionManager.GetModule("PVE") as PveModule;
                    if (pveModuleUL != null)
                    {
                        pveModuleUL.ToggleSwitch(ref PveModule.Outbound, true);
                        Logger.Info("Swapper: Activated PVE Upload (3074 UL)");
                    }
                    break;
                    
                case "3074 DL": // PVE Download  
                    var pveModuleDL = InterceptionManager.GetModule("PVE") as PveModule;
                    if (pveModuleDL != null)
                    {
                        pveModuleDL.ToggleSwitch(ref PveModule.Inbound, true);
                        Logger.Info("Swapper: Activated PVE Download (3074 DL)");
                    }
                    break;
                    
                case "27k UL": // PVP Upload
                    var pvpModule = InterceptionManager.GetModule("PVP") as PvpModule;
                    if (pvpModule != null)
                    {
                        pvpModule.ToggleSwitch(ref PvpModule.Outbound, true);
                        Logger.Info("Swapper: Activated PVP Upload (27k UL)");
                    }
                    break;
                    
                default:
                    Logger.Warning($"Swapper: Unknown module activation: {activateModule}");
                    break;
            }
        }
        
        
        
        /// <summary>
        /// Finalizes the swap sequence by deactivating modules and clicking final loadout
        /// </summary>
        private async Task FinalizeSwapSequence()
        {
            Logger.Info("Swapper: Finalizing swap sequence");
            
            // Move to final loadout position
            if (finalLoadoutNumber >= 1 && finalLoadoutNumber <= 12)
            {
                var finalCoord = GetLoadoutCoordinate(finalLoadoutNumber);
                MouseMove(finalCoord.x, finalCoord.y);
                Logger.Info($"Swapper: Moving to final loadout {finalLoadoutNumber} at ({finalCoord.x}, {finalCoord.y})");
            }
            
            // Deactivate modules first (before rapid clicking)
            DeactivateSelectedModule();
            
            // Wait 300ms before rapid clicking (matches AHK line 580)
            PreciseSleep(300);
            
            // Perform the rapid clicking sequence that matches the AHK script (lines 581-592)
            MouseClick(); // First click
            PreciseSleep(60);
            MouseClick();
            PreciseSleep(50);
            MouseClick();
            PreciseSleep(40);
            MouseClick();
            PreciseSleep(30);
            MouseClick();
            PreciseSleep(20);
            MouseClick();
            PreciseSleep(10);
            // Final click (no delay after)
            
            Logger.Info("Swapper: Completed rapid final click sequence");
            
            // Optional F1 key press if enabled
            if (f1AfterSwaps)
            {
                SendKey(Keys.F1);
                Logger.Debug("Swapper: Sent F1 key after swap completion");
            }
        }
        
        /// <summary>
        /// Deactivates the currently selected module
        /// </summary>
        private void DeactivateSelectedModule()
        {
            if (activateModule == "None")
            {
                return;
            }
            
            Logger.Info($"Swapper: Deactivating module: {activateModule}");
            
            switch (activateModule)
            {
                case "3074 UL": // PVE Upload
                    var pveModuleUL = InterceptionManager.GetModule("PVE") as PveModule;
                    if (pveModuleUL != null)
                    {
                        pveModuleUL.ToggleSwitch(ref PveModule.Outbound, false);
                        Logger.Info("Swapper: Deactivated PVE Upload (3074 UL)");
                    }
                    break;
                    
                case "3074 DL": // PVE Download  
                    var pveModuleDL = InterceptionManager.GetModule("PVE") as PveModule;
                    if (pveModuleDL != null)
                    {
                        pveModuleDL.ToggleSwitch(ref PveModule.Inbound, false);
                        Logger.Info("Swapper: Deactivated PVE Download (3074 DL)");
                    }
                    break;
                    
                case "27k UL": // PVP Upload
                    var pvpModule = InterceptionManager.GetModule("PVP") as PvpModule;
                    if (pvpModule != null)
                    {
                        pvpModule.ToggleSwitch(ref PvpModule.Outbound, false);
                        Logger.Info("Swapper: Deactivated PVP Upload (27k UL)");
                    }
                    break;
            }
        }
        
        #region Keybind Handler Methods
        
        /// <summary>
        /// Handler for 3074 UL (PVE Upload) keybind - activates swapper with PVE Upload
        /// </summary>
        private void Module3074ULKeybindHandler(LinkedList<Keycode> keycodes)
        {
            // Skip KeybindChecks() for swapper - we want one-shot triggers, not hold-to-activate
            if (!Module3074ULKeybind.Any() || keycodes.Count < Module3074ULKeybind.Count) return;
            
            if (Module3074ULKeybind.All(x => keycodes.Contains(x)))
            {
                // Only trigger if not already running
                if (!IsActivated)
                {
                    Logger.Info("Swapper: 3074 UL keybind pressed - starting swapper with PVE Upload");
                    SetActivateModule("3074 UL");
                    Toggle(); // Start the swapper
                }
            }
        }
        
        /// <summary>
        /// Handler for 3074 DL (PVE Download) keybind - activates swapper with PVE Download
        /// </summary>
        private void Module3074DLKeybindHandler(LinkedList<Keycode> keycodes)
        {
            // Skip KeybindChecks() for swapper - we want one-shot triggers, not hold-to-activate
            if (!Module3074DLKeybind.Any() || keycodes.Count < Module3074DLKeybind.Count) return;
            
            if (Module3074DLKeybind.All(x => keycodes.Contains(x)))
            {
                // Only trigger if not already running
                if (!IsActivated)
                {
                    Logger.Info("Swapper: 3074 DL keybind pressed - starting swapper with PVE Download");
                    SetActivateModule("3074 DL");
                    Toggle(); // Start the swapper
                }
            }
        }
        
        /// <summary>
        /// Handler for 27k UL (PVP Upload) keybind - activates swapper with PVP Upload
        /// </summary>
        private void Module27kULKeybindHandler(LinkedList<Keycode> keycodes)
        {
            // Skip KeybindChecks() for swapper - we want one-shot triggers, not hold-to-activate
            if (!Module27kULKeybind.Any() || keycodes.Count < Module27kULKeybind.Count) return;
            
            if (Module27kULKeybind.All(x => keycodes.Contains(x)))
            {
                // Only trigger if not already running
                if (!IsActivated)
                {
                    Logger.Info("Swapper: 27k UL keybind pressed - starting swapper with PVP Upload");
                    SetActivateModule("27k UL");
                    Toggle(); // Start the swapper
                }
            }
        }
        
        #endregion
    }
}
