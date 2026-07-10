using InfinLimit.Models;
using InfinLimit.Utility;
using InfinLimit.Interception;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using InfinLimit.Interception.Modules;
using System.Xml.Linq;
using System.Reflection;
using System.Diagnostics;

namespace InfinLimit
{
    public static class Config
    {
        static string ConfigPath = "infinlimit.cfg";
        public static ConfigModel Instance { get; set; }

        static Config()
        {
            ConfigPath = Path.Combine(App.ExeDirectory, ConfigPath);
            Instance = File.Exists(ConfigPath)
                ? File.ReadAllText(ConfigPath).Deserialize<ConfigModel>()
                : Instance ?? new ConfigModel() { Volume = 30 };
        }

        public static ModuleSettingsBase GetNamed(string name)
        {
            if (Instance == null)
                Load();

            if (Instance.Modules.TryGetValue(name, out var module))
                return module;

            Instance.Modules[name] = new ModuleSettingsBase();
            return Instance.Modules[name];
        }
        public static void Load()
        {
            Instance = File.Exists(ConfigPath)
                ? File.ReadAllText(ConfigPath).Deserialize<ConfigModel>()
                : Instance ?? new ConfigModel() { Volume = 30 };

            App.snow = Instance.Settings.Window_Snow;
            Logger.Info("Config loaded");
        }

        public static void Save()
        {
            if (Instance == null)
                return;

            try
            {
                GetNamed("PVE").Settings["Buffer"] = PveModule.Buffer;
                GetNamed("PVE").Settings["AutoResync"] = PveModule.AutoResync;
                GetNamed("PVE").Settings["OutboundKeybind"] = PveModule.OutboundKeybind;
                GetNamed("PVE").Settings["SlowInboundKeybind"] = PveModule.SlowInboundKeybind;
                GetNamed("PVE").Settings["SlowOutboundKeybind"] = PveModule.SlowOutboundKeybind;

                GetNamed("PVP").Settings["OutboundKeybind"] = PvpModule.OutboundKeybind;
                GetNamed("PVP").Settings["Buffer"] = PvpModule.Buffer;
                GetNamed("PVP").Settings["AutoResync"] = PvpModule.AutoResync;

                GetNamed("API Block").Settings["SelfDisable"] = ApiModule.Disable;
                GetNamed("API Block").Settings["Buffer"] = ApiModule.Buffer;
                GetNamed("З0K").Settings["Buffer"] = InstanceModule.Buffer;
                GetNamed("Reconnect").Settings["HoldSeconds"] = ReconnectModule.HoldSeconds;

                GetNamed("Multishot").Settings["Inbound"] = MultishotModule.Inbound;
                GetNamed("Multishot").Settings["Outbound"] = MultishotModule.Outbound;
                GetNamed("Multishot").Settings["TimeLimit"] = MultishotModule.MaxTime;
                GetNamed("Multishot").Settings["PlayersMode"] = MultishotModule.PlayersMode;
                GetNamed("Multishot").Settings["ShotDetection"] = MultishotModule.WaitShot;
                GetNamed("Multishot").Settings["Togglable"] = InterceptionManager.GetModule("Multishot").Togglable;
                GetNamed("Multishot").Settings["PlayersKeybind"] = MultishotModule.PlayersKeybind;

                // SwapperModule: serialize all 5 profiles as a JSON string
                GetNamed("Swapper").Settings["Profiles"] = JsonSerializer.Serialize(SwapperModule.Profiles);

                // ConsoleModule keybinds
                GetNamed("ConsoleModule").Settings["EnableKeybind"]      = ConsoleModule.EnableKeybind;
                GetNamed("ConsoleModule").Settings["DLKeybind"]          = ConsoleModule.DLKeybind;
                GetNamed("ConsoleModule").Settings["ULKeybind"]          = ConsoleModule.ULKeybind;
                GetNamed("ConsoleModule").Settings["DLSlowKeybind"]      = ConsoleModule.DLSlowKeybind;
                GetNamed("ConsoleModule").Settings["ULSlowKeybind"]      = ConsoleModule.ULSlowKeybind;
                GetNamed("ConsoleModule").Settings["AutoResyncKeybind"]  = ConsoleModule.AutoResyncKeybind;
                GetNamed("ConsoleModule").Settings["BufferingKeybind"]   = ConsoleModule.BufferingKeybind;
                GetNamed("ConsoleModule").Settings["GamePauseKeybind"]   = ConsoleModule.GamePauseKeybind;
                GetNamed("ConsoleModule").Settings["PortFilterKeybind"]  = ConsoleModule.PortFilterKeybind;
                GetNamed("ConsoleModule").Settings["Port2FilterKeybind"] = ConsoleModule.Port2FilterKeybind;

                File.WriteAllText(ConfigPath, Instance.Serialize(true));
                Logger.Info($"Config saved");
            }
            catch (Exception e)
            {
                Logger.Error(e, additionalInfo: "Config save");
            }
        }
    }
}
