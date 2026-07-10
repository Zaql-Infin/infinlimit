using InfinLimit.Utility;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace InfinLimit.Models
{
    public class ConfigModel
    {
        public string CurrentModule { get; set; }
        public double Volume { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public Settings Settings { get; set; } = new Settings();
        public Dictionary<string, ModuleSettingsBase> Modules { get; set; } = new Dictionary<string, ModuleSettingsBase>();
        public List<string> LastOpenAhks { get; set; } = new List<string>();
    }

    public class Settings
    {
        public string? AccentHex { get; set; } = null;

        public string? Tracker_BungieName { get; set; } = null;
        public bool Tracker_CountRaids { get; set; } = true;
        public bool Tracker_CountDungeons { get; set; } = true;
        public bool AltTabSupressKeybinds { get; set; } = false;

        public bool Overlay_StartOnLaunch { get; set; } = true;
        public bool Overlay_ShowTime { get; set; } = false;
        public bool Overlay_ShowTimer { get; set; } = true;
        public bool Overlay_DisableOnInactivity { get; set; } = true;
        public bool Overlay_DisplayOnlyTogglable { get; set; } = true;
        public int Overlay_LeftOffset { get; set; } = 0;
        public int Overlay_BottomOffset { get; set; } = 0;
        public bool Overlay_HideFromCapture { get; set; } = false;
        public bool Overlay_FreePosition { get; set; } = false;
        public double Overlay_FreeX { get; set; } = 24;
        public double Overlay_FreeY { get; set; } = 800;

        public bool Window_Snow { get; set; } = true;
        public bool Window_DisplayClock { get; set; } = true;
        public bool Window_DisplaySpeed { get; set; } = true;
        public int Window_TimerDecaySeconds { get; set; } = 10;

        public bool DB_SavePackets { get; set; } = true;
        public bool DB_KeyPresses { get; set; } = true;

        public bool AHK_AutoClose { get; set; } = true;
        public bool AHK_AutoOpen { get; set; } = true;

        public List<Keycode> ZaqlMotes_Keybind { get; set; } = new List<Keycode>();
    }

    public class ModuleSettingsBase
    {
        public bool Enabled = false;
        public List<Keycode> Keybind = new List<Keycode>();
        public Dictionary<string, object> Settings = new Dictionary<string, object>();

        public T GetSettings<T>(string name)
        {
            if (!Settings.ContainsKey(name))
            {
                Settings[name] = default(T);
                if (name == "Inbound" || name == "SelfDisable" || name == "Buffer" || name == "AutoResync")
                    Settings[name] = true;
                else if (name.Contains("Keybind"))
                    Settings[name] = new List<Keycode>();
                else if (name == "TimeLimit")
                    Settings[name] = 1.8d;
                // SwapperModule specific defaults
                else if (name == "Use3074Upload")
                    Settings[name] = true;
                else if (name == "Use3074Download" || name == "Use27kUpload" || name == "F1AfterSwaps"
                      || name == "AutoDisableBuffering" || name == "FullGame" || name == "OpenInventory")
                    Settings[name] = false;
                else if (name == "DamageLoadout" || name == "FinalLoadoutNumber")
                    Settings[name] = 1;     // Default to loadout 1
                else if (name == "LoopDuration" || name == "SwapTimeOverall")
                    Settings[name] = 5000;  // Default to 5000ms
                else if (name == "DelayBetweenLoadouts")
                    Settings[name] = 25;
                else if (name == "UntickDelay")
                    Settings[name] = 500;
                else if (name == "ActivateModule")
                    Settings[name] = "3074 UL";  // Default to 3074 UL
                else if (name == "SelectedLoadouts")
                    Settings[name] = new List<int>(); // Default to empty list

                Config.Save();
            }

            if (Settings[name] is JsonElement e)
            {
                if (name.Contains("Keybind"))
                {
                    return JsonSerializer.Deserialize<T>(e);
                }

                if (typeof(T).Equals(typeof(bool)))
                {
                    return (T)Convert.ChangeType(e.GetBoolean(), typeof(T));
                }

                if (typeof(T).Equals(typeof(double)))
                {
                    return (T)Convert.ChangeType(e.GetDouble(), typeof(T));
                }
                
                if (typeof(T).Equals(typeof(int)))
                {
                    return (T)Convert.ChangeType(e.GetInt32(), typeof(T));
                }
                
                if (typeof(T).Equals(typeof(string)))
                {
                    return (T)Convert.ChangeType(e.GetString(), typeof(T));
                }
            }

            // Handle the case where the stored value might not implement IConvertible
            var storedValue = Settings[name];
            
            // If the stored value is already the correct type, return it directly
            if (storedValue is T directValue)
            {
                return directValue;
            }
            
            // Try to use Convert.ChangeType safely
            try
            {
                if (storedValue != null && storedValue is IConvertible)
                {
                    return (T)Convert.ChangeType(storedValue, typeof(T));
                }
                
                // If not convertible, return default for the type
                return default(T);
            }
            catch (Exception)
            {
                // Fallback to default value if conversion fails
                return default(T);
            }
        }
    }
}
