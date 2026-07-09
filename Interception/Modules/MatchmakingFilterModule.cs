using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

using InfinLimit.Models;

namespace InfinLimit.Interception.Modules
{
    public class MatchmakingFilterModule : PacketModuleBase
    {
        public enum Mode { Blacklist, Whitelist }

        public static Mode FilterMode = Mode.Blacklist;
        private static readonly string RulesPath = Path.Combine(App.ExeDirectory, "matchmaking_rules.txt");

        private readonly HashSet<string> _rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _rulesLock = new object();
        private FileSystemWatcher _watcher;

        public MatchmakingFilterModule()
            : base("Matchmaking Filter", true, InterceptionManager.GetProvider("Players"))
        {
            Icon = System.Windows.Application.Current.FindResource("DefaultIcon") as Geometry;
            Description = "IP blacklist/whitelist for matchmaking peer connections\nEdit matchmaking_rules.txt — one IP per line";
            try
            {
                FilterMode = Config.GetNamed(Name).GetSettings<int>("FilterMode") == 1
                    ? Mode.Whitelist
                    : Mode.Blacklist;
            }
            catch { }
            LoadRules();
            SetupWatcher();
        }

        public override void StopListening()
        {
            _watcher?.Dispose();
            _watcher = null;
            base.StopListening();
        }

        private void SetupWatcher()
        {
            try
            {
                EnsureRulesFileExists();
                _watcher = new FileSystemWatcher(App.ExeDirectory, Path.GetFileName(RulesPath))
                {
                    NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += (_, _) => LoadRules();
                _watcher.Created += (_, _) => LoadRules();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MatchmakingFilterModule watcher setup");
            }
        }

        private static void EnsureRulesFileExists()
        {
            try
            {
                if (!File.Exists(RulesPath))
                    File.WriteAllText(RulesPath,
                        "# One IP per line. Lines starting with # are ignored.\n" +
                        "# Blacklist mode (default): listed IPs are blocked.\n" +
                        "# Whitelist mode: only listed IPs are allowed.\n");
            }
            catch { }
        }

        private void LoadRules()
        {
            try
            {
                if (!File.Exists(RulesPath)) return;
                var lines = File.ReadAllLines(RulesPath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"));
                lock (_rulesLock)
                {
                    _rules.Clear();
                    foreach (var line in lines) _rules.Add(line);
                }
                Logger.Debug($"{Name}: reloaded {_rules.Count} rule(s)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MatchmakingFilterModule rule reload");
            }
        }

        public override void Toggle() => IsActivated = !IsActivated;

        public override bool AllowPacket(Packet p)
        {
            if (!base.AllowPacket(p)) return false;
            if (!IsActivated) return true;

            string ip = (p.Inbound ? p.SrcAddr : p.DstAddr)?.ToString();
            if (string.IsNullOrEmpty(ip)) return true;

            bool inList;
            lock (_rulesLock) { inList = _rules.Contains(ip); }

            return FilterMode == Mode.Blacklist ? !inList : inList;
        }
    }
}
