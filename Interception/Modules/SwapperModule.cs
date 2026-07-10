using InfinLimit.Models;
using InfinLimit.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        
        #region Profiles

        public static SwapProfile[] Profiles = new SwapProfile[5]
        {
            new() { Name = "Profile 1", Port3074 = true },
            new() { Name = "Profile 2", Port3074 = true },
            new() { Name = "Profile 3", Port3074 = true },
            new() { Name = "Profile 4", Port3074 = true },
            new() { Name = "Profile 5", Port3074 = true },
        };

        public static int CurrentProfileIndex = 0;

        // Set true while a keybind button is in capture mode so profile hotkeys don't fire mid-setup
        public static bool IsCapturingKeybind = false;

        #endregion

        public SwapperModule() : base("Swapper", true)
        {
            Icon = System.Windows.Application.Current.FindResource("SwapperIcon") as Geometry;
            Description = "Advanced loadout swapper with pixel detection, precise timing, and module integration (PVE/PVP activation).";

            InitializeResolutionAndCoordinates();
            LoadSettings();

            KeyListener.KeysPressed += ProfileKeybindHandler;
        }
        
        private void LoadSettings()
        {
            var config = Config.GetNamed("Swapper");
            var profilesJson = config.GetSettings<string>("Profiles");
            if (!string.IsNullOrEmpty(profilesJson))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<SwapProfile[]>(profilesJson);
                    if (loaded != null)
                        for (int i = 0; i < Math.Min(5, loaded.Length); i++)
                            if (loaded[i] != null) Profiles[i] = loaded[i];
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Swapper: Profile load failed: {ex.Message}");
                }
            }
            Logger.Info("Swapper: Profiles loaded");
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
        

        // 1920x1080 base loadout X/Y positions (4-column grid, 5 rows = 20 loadouts)
        private static readonly int[] Base1080X = { 119, 212, 306, 400, 119, 212, 306, 400, 119, 212, 306, 400, 119, 212, 306, 400, 119, 212, 306, 400 };
        private static readonly int[] Base1080Y = { 387, 387, 387, 387, 481, 481, 481, 481, 575, 575, 575, 575, 668, 668, 668, 668, 762, 762, 762, 762 };

        private void Set1920x1080Coordinates()
        {
            for (int i = 0; i < 20; i++)
                coordinates[$"load{i + 1}"] = (Base1080X[i], Base1080Y[i]);
            loadColorCoord = (77, 104);
            invColorCoord = (960, 1035);
            invColor2Coord = (960, 1014);
        }

        private void Set2560x1440Coordinates()
        {
            for (int i = 0; i < 20; i++)
                coordinates[$"load{i + 1}"] = ((int)(Base1080X[i] * (1440.0 / 1080.0)), (int)(Base1080Y[i] * (1440.0 / 1080.0)));
            loadColorCoord = (102, 139);
            invColorCoord = (1280, 1380);
            invColor2Coord = (1280, 1351);
        }

        private void SetScaledCoordinates()
        {
            hwMultiplier = (double)screenHeight / 1080.0;
            wOffset = (screenWidth - 1920.0 * hwMultiplier) / 2;

            Logger.Info($"Swapper: Scaling - hwMultiplier: {hwMultiplier:F3}, wOffset: {wOffset:F1}");

            for (int i = 0; i < 20; i++)
                coordinates[$"load{i + 1}"] = (ScaleX(Base1080X[i]), ScaleY(Base1080Y[i]));

            loadColorCoord = (ScaleX(77), ScaleY(104));
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
            if (loadoutNumber < 1 || loadoutNumber > 20)
            {
                Logger.Warning($"Swapper: Invalid loadout number {loadoutNumber}, using loadout 1");
                return coordinates["load1"];
            }

            return coordinates[$"load{loadoutNumber}"];
        }

        public void UpdateDamageLoadoutCoordinate(int loadoutNumber)
        {
            if (loadoutNumber >= 1 && loadoutNumber <= 20)
            {
                loadDualCoord = GetLoadoutCoordinate(loadoutNumber);
                Logger.Info($"Swapper: Set damage loadout to {loadoutNumber} at coordinates ({loadDualCoord.x}, {loadDualCoord.y})");
            }
        }
        

        /// Gets all current coordinates for debugging

        public void LogAllCoordinates()
        {
            Logger.Info($"Swapper Coordinates (Resolution: {screenWidth}x{screenHeight}):");
            for (int i = 1; i <= 20; i++)
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
        public void AlternateClickThroughLoadouts(List<int> loadoutNumbers, int delayPerClickMs, int durationMs)
        {
            if (!loadoutNumbers.Any())
            {
                Logger.Warning("Swapper: No loadouts provided for alternating clicks");
                return;
            }

            // The delay sits BEFORE the click so the game always has at least this many ms
            // to see the cursor at the new position before the click fires.
            // 16 ms = one frame at 60 fps. Raise SwapDelay in the UI for lower FPS.
            int safeDelay = Math.Max(delayPerClickMs, 16);

            Logger.Info($"Swapper: Alternating clicks — {loadoutNumbers.Count} loadouts, {safeDelay}ms pre-click, {durationMs}ms total");

            var coords = loadoutNumbers.Select(n => GetLoadoutCoordinate(n)).ToArray();
            long start = GetPreciseTimestamp();
            int idx = 0, clicks = 0;

            while (GetPreciseTimestamp() - start < durationMs && IsActivated)
            {
                var (x, y) = coords[idx];

                // Move cursor to the target loadout position.
                SetCursorPos(x, y);

                // Wait the full configured delay so the game registers the new cursor
                // position before the click arrives — this is the FPS-independence fix.
                PreciseSleep(safeDelay);

                // Fire the click at the current cursor position.
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero);
                clicks++;

                Logger.Debug($"Swapper: Click #{clicks} loadout {loadoutNumbers[idx]} ({x},{y})");
                idx = (idx + 1) % coords.Length;
            }

            Logger.Info($"Swapper: Done — {clicks} clicks in {GetPreciseTimestamp() - start}ms");
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
        private bool IsPixelBright(int x, int y)
        {
            uint color = GetPixelColor(x, y);
            byte r = (byte)(color & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)((color >> 16) & 0xFF);
            return r >= 208 && g >= 208 && b >= 208;
        }

        // Opens D2 inventory and navigates to the loadout screen using pixel detection.
        // Mirrors DarkLimiter's OpenLoadouts(): presses F1, then LEFT arrow up to 27 times
        // until the loadout color pixel turns bright (loadout tab is active).
        private void OpenInventory()
        {
            SendKey(Keys.F1);
            Thread.Sleep(200);

            for (int i = 0; i < 27; i++)
            {
                Thread.Sleep(1);
                SetCursorPos(10, screenHeight / 2);
                SendKey(Keys.Left);
                if (IsPixelBright(loadColorCoord.x, loadColorCoord.y))
                    break;
            }

            // Wait up to 500ms for the loadout panel to fully appear
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (!IsPixelBright(loadColorCoord.x, loadColorCoord.y) && DateTime.UtcNow < deadline)
                Thread.Sleep(5);
        }

        #endregion

        #region Execution

        public override void Toggle()
        {
            if (IsActivated) return;
            TriggerProfile(CurrentProfileIndex);
        }

        public void TriggerProfile(int idx)
        {
            if (idx < 0 || idx >= 5 || IsActivated) return;
            IsActivated = true;
            Logger.Info($"Swapper: Triggering profile {idx + 1}: {Profiles[idx].Name}");
            Task.Run(async () =>
            {
                try { await ExecuteSwapSequenceWithModules(Profiles[idx]); }
                catch (Exception ex) { Logger.Error(ex, additionalInfo: "Swapper execution error"); }
                finally { IsActivated = false; }
            });
        }

        private void ActivateModules(SwapProfile p)
        {
            if (p.Port3074) (InterceptionManager.GetModule("PVE") as PveModule)?.ToggleSwitch(ref PveModule.Outbound, true);
            if (p.Packet3074DL) (InterceptionManager.GetModule("PVE") as PveModule)?.ToggleSwitch(ref PveModule.Inbound, true);
            if (p.Port27k) (InterceptionManager.GetModule("PVP") as PvpModule)?.ToggleSwitch(ref PvpModule.Outbound, true);
        }

        private void DeactivateModules(SwapProfile p)
        {
            if (p.Port3074) (InterceptionManager.GetModule("PVE") as PveModule)?.ToggleSwitch(ref PveModule.Outbound, false);
            if (p.Packet3074DL) (InterceptionManager.GetModule("PVE") as PveModule)?.ToggleSwitch(ref PveModule.Inbound, false);
            if (p.Port27k) (InterceptionManager.GetModule("PVP") as PvpModule)?.ToggleSwitch(ref PvpModule.Outbound, false);
        }

        private async Task ExecuteSwapSequenceWithModules(SwapProfile profile)
        {
            Logger.Info($"Swapper: Starting sequence for '{profile.Name}'");
            long start = GetPreciseTimestamp();

            var selectedLoadouts = profile.LoadoutEnabled
                .Select((on, i) => on ? i + 1 : 0)
                .Where(n => n > 0)
                .ToList();

            if (!selectedLoadouts.Any())
            {
                Logger.Warning("Swapper: No loadouts selected");
                return;
            }

            bool savedBuffer = false;
            if (profile.AutoDisableBuffering && PveModule.Buffer)
            {
                savedBuffer = true;
                PveModule.Buffer = false;
                Logger.Info("Swapper: Auto-disabled PVE buffering");
            }

            ActivateModules(profile);

            var fg = InterceptionManager.GetModule("Full Game") as FullGameModule;
            if (profile.FullGame && fg != null && !fg.IsActivated)
                fg.ForceEnable();

            if (profile.OpenInventory)
                await Task.Run(() => OpenInventory());

            if (profile.SwapDelay > 0)
                await Task.Delay(profile.SwapDelay);

            await Task.Run(() => AlternateClickThroughLoadouts(selectedLoadouts, profile.SwapDelay, profile.SwapDuration));

            await FinalizeSwapSequence(profile);

            if (profile.CloseInventory)
                SendKey(Keys.F1);

            if (profile.UntickDelay > 0)
                await Task.Delay(profile.UntickDelay);

            if (savedBuffer)
            {
                PveModule.Buffer = true;
                Logger.Info("Swapper: Restored PVE buffering");
            }

            if (profile.FullGame && fg != null && fg.IsActivated)
                fg.ForceDisable();

            Logger.Info($"Swapper: Sequence complete in {GetPreciseTimestamp() - start}ms");
        }

        private async Task FinalizeSwapSequence(SwapProfile profile)
        {
            var (fx, fy) = GetLoadoutCoordinate(profile.EndingLoadout);
            SetCursorPos(fx, fy);

            DeactivateModules(profile);

            PreciseSleep(300);

            // Rapid-fire clicks to confirm the ending loadout.
            // Cursor is already positioned; each click is just down+up.
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(60);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(40);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(30);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(20);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero); PreciseSleep(10);

            Logger.Info($"Swapper: Final rapid-click done at loadout {profile.EndingLoadout} ({fx},{fy})");
        }

        private void ProfileKeybindHandler(LinkedList<Keycode> keycodes)
        {
            if (IsActivated || IsCapturingKeybind) return;

            // Pick the most-specific match (most keybind keys) so a longer combo
            // like [F1+F2] beats a shorter one like [F1] when both would match.
            int bestIdx = -1, bestCount = 0;
            for (int i = 0; i < 5; i++)
            {
                var kb = Profiles[i].Keybind;
                if (kb.Count > 0 && keycodes.Count >= kb.Count && kb.All(x => keycodes.Contains(x)))
                {
                    if (kb.Count > bestCount) { bestCount = kb.Count; bestIdx = i; }
                }
            }
            if (bestIdx >= 0)
                TriggerProfile(bestIdx);
        }

        #endregion

    }
}
