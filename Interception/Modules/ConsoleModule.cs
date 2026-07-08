using InfinLimit.Utility;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using WindivertDotnet;

namespace InfinLimit.Interception.Modules
{
    public class ConsoleModule
    {
        public static List<ConsoleDevice> Devices { get; } = new();
        public bool IsEnabled { get; private set; }

        // Global toggle flags (toggled via keybinds)
        public static bool DLEnabled = true;
        public static bool ULEnabled = true;
        public static bool DLSlowEnabled = false;
        public static bool ULSlowEnabled = false;
        public static bool AutoResync = false;
        public static bool Buffering = false;

        // Slow mode speed multiplier (10% of set rate)
        public const float SlowMultiplier = 0.10f;

        // Keybind lists — loaded/saved via Config
        public static List<Keycode> EnableKeybind = new();
        public static List<Keycode> DLKeybind = new();
        public static List<Keycode> ULKeybind = new();
        public static List<Keycode> DLSlowKeybind = new();
        public static List<Keycode> ULSlowKeybind = new();
        public static List<Keycode> AutoResyncKeybind = new();
        public static List<Keycode> BufferingKeybind = new();

        private readonly ConcurrentDictionary<string, TokenBucket> _uploadBuckets = new();
        private readonly ConcurrentDictionary<string, TokenBucket> _downloadBuckets = new();
        private WinDivert? _divert;
        private CancellationTokenSource? _cts;

        // Game filter — null means "All Traffic"
        public static GameProfile? SelectedGame;

        public static readonly List<GameProfile> GameProfiles = new()
        {
            new GameProfile("All Traffic",   new()),
            new GameProfile("Destiny 2",     new() { 3074, 3478, 3479, 3480, 7500, 9308 }),
            new GameProfile("Call of Duty",  new() { 3074, 3075, 3478, 3479, 27015, 27016 }),
            new GameProfile("Fortnite",      new() { 9000, 9010, 9020, 9030, 22222 }),
            new GameProfile("GTA Online",    new() { 6672, 61455, 61456, 61457, 61458 }),
            new GameProfile("Halo Infinite", new() { 3074, 3478, 3479 }),
            new GameProfile("Apex Legends",  new() { 37015, 37016, 37017 }),
        };

        // Reference back to the panel for UI updates
        public static Action? OnStateChanged;

        public ConsoleModule()
        {
            var cfg = Config.GetNamed("ConsoleModule");

            var savedGame = cfg.GetSettings<string>("SelectedGame");
            SelectedGame = GameProfiles.Find(g => g.Name == savedGame) ?? GameProfiles[1]; // default Destiny 2

            // Keybinds — "Keybind" suffix triggers correct JSON deserialization in GetSettings
            EnableKeybind.AddRange(cfg.GetSettings<List<Keycode>>("EnableKeybind") ?? new());
            DLKeybind.AddRange(cfg.GetSettings<List<Keycode>>("DLKeybind") ?? new());
            ULKeybind.AddRange(cfg.GetSettings<List<Keycode>>("ULKeybind") ?? new());
            DLSlowKeybind.AddRange(cfg.GetSettings<List<Keycode>>("DLSlowKeybind") ?? new());
            ULSlowKeybind.AddRange(cfg.GetSettings<List<Keycode>>("ULSlowKeybind") ?? new());
            AutoResyncKeybind.AddRange(cfg.GetSettings<List<Keycode>>("AutoResyncKeybind") ?? new());
            BufferingKeybind.AddRange(cfg.GetSettings<List<Keycode>>("BufferingKeybind") ?? new());

            // Bool flags — check key exists first to apply our own defaults
            DLEnabled = cfg.Settings.ContainsKey("DLEnabled") ? cfg.GetSettings<bool>("DLEnabled") : true;
            ULEnabled = cfg.Settings.ContainsKey("ULEnabled") ? cfg.GetSettings<bool>("ULEnabled") : true;
            AutoResync = cfg.Settings.ContainsKey("AutoResync") ? cfg.GetSettings<bool>("AutoResync") : false;
            Buffering = cfg.Settings.ContainsKey("Buffering") ? cfg.GetSettings<bool>("Buffering") : false;

            KeyListener.KeysPressed += KeybindHandler;
        }

        private void KeybindHandler(LinkedList<Keycode> keys)
        {
            if (EnableKeybind.Count > 0 && MatchesKeybind(keys, EnableKeybind))
            {
                if (IsEnabled) Disable(); else Enable();
                OnStateChanged?.Invoke();
                Config.Save();
                return;
            }
            if (DLKeybind.Count > 0 && MatchesKeybind(keys, DLKeybind))
            {
                DLEnabled = !DLEnabled;
                if (DLEnabled) DLSlowEnabled = false;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
            if (ULKeybind.Count > 0 && MatchesKeybind(keys, ULKeybind))
            {
                ULEnabled = !ULEnabled;
                if (ULEnabled) ULSlowEnabled = false;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
            if (DLSlowKeybind.Count > 0 && MatchesKeybind(keys, DLSlowKeybind))
            {
                DLSlowEnabled = !DLSlowEnabled;
                if (DLSlowEnabled) DLEnabled = false;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
            if (ULSlowKeybind.Count > 0 && MatchesKeybind(keys, ULSlowKeybind))
            {
                ULSlowEnabled = !ULSlowEnabled;
                if (ULSlowEnabled) ULEnabled = false;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
            if (AutoResyncKeybind.Count > 0 && MatchesKeybind(keys, AutoResyncKeybind))
            {
                AutoResync = !AutoResync;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
            if (BufferingKeybind.Count > 0 && MatchesKeybind(keys, BufferingKeybind))
            {
                Buffering = !Buffering;
                SaveFlags(); OnStateChanged?.Invoke(); return;
            }
        }

        private static bool MatchesKeybind(LinkedList<Keycode> pressed, List<Keycode> bind)
        {
            if (pressed.Count < bind.Count) return false;
            foreach (var k in bind)
                if (!pressed.Contains(k)) return false;
            return true;
        }

        public void UnhookKeybind() => KeyListener.KeysPressed -= KeybindHandler;
        public void RehookKeybind() => KeyListener.KeysPressed += KeybindHandler;

        public static void SaveFlags()
        {
            var cfg = Config.GetNamed("ConsoleModule");
            cfg.Settings["DLEnabled"] = DLEnabled;
            cfg.Settings["ULEnabled"] = ULEnabled;
            cfg.Settings["AutoResync"] = AutoResync;
            cfg.Settings["Buffering"] = Buffering;
            Config.Save();
        }

        public static void SaveGame()
        {
            var cfg = Config.GetNamed("ConsoleModule");
            cfg.Settings["SelectedGame"] = SelectedGame?.Name ?? "All Traffic";
            Config.Save();
        }

        public static void SaveKeybinds()
        {
            var cfg = Config.GetNamed("ConsoleModule");
            cfg.Settings["EnableKeybind"] = EnableKeybind;
            cfg.Settings["DLKeybind"] = DLKeybind;
            cfg.Settings["ULKeybind"] = ULKeybind;
            cfg.Settings["DLSlowKeybind"] = DLSlowKeybind;
            cfg.Settings["ULSlowKeybind"] = ULSlowKeybind;
            cfg.Settings["AutoResyncKeybind"] = AutoResyncKeybind;
            cfg.Settings["BufferingKeybind"] = BufferingKeybind;
            Config.Save();
        }

        public void Enable()
        {
            if (IsEnabled) return;
            IsEnabled = true;
            _cts = new CancellationTokenSource();
            Task.Run(() => ProcessPacketsAsync(_cts.Token));
        }

        public void Disable()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            _cts?.Cancel();
            try { _divert?.Dispose(); } catch { }
            _divert = null;
        }

        private async Task ProcessPacketsAsync(CancellationToken ct)
        {
            try
            {
                _divert = new WinDivert("ip", WinDivertLayer.Network, priority: 100);

                while (!ct.IsCancellationRequested)
                {
                    var packet = new WinDivertPacket();
                    var addr = new WinDivertAddress();

                    var recvLength = await _divert.RecvAsync(packet, addr, ct);
                    if (ct.IsCancellationRequested) break;

                    if (!TryGetIpAddresses(packet.Span, out var srcIp, out var dstIp))
                    {
                        await _divert.SendAsync(packet, addr);
                        continue;
                    }

                    // Skip packet if it doesn't match the selected game's ports
                    if (SelectedGame != null && SelectedGame.Ports.Count > 0
                        && !MatchesGamePorts(packet.Span, SelectedGame.Ports))
                    {
                        await _divert.SendAsync(packet, addr);
                        continue;
                    }

                    bool dropped = false;

                    foreach (var device in Devices)
                    {
                        if (!device.Enabled || device.IP == null) continue;

                        // Traffic FROM device = upload
                        if (srcIp == device.IP && (ULEnabled || ULSlowEnabled) && device.UploadKbps > 0)
                        {
                            float mult = ULSlowEnabled ? SlowMultiplier : 1f;
                            long rate = (long)(device.UploadKbps * 1000L / 8 * mult);
                            var bucket = _uploadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                            bucket.Rate = rate;
                            if (!bucket.Consume(packet.Length)) { dropped = true; break; }
                        }

                        // Traffic TO device = download
                        if (dstIp == device.IP && (DLEnabled || DLSlowEnabled) && device.DownloadKbps > 0)
                        {
                            float mult = DLSlowEnabled ? SlowMultiplier : 1f;
                            long rate = (long)(device.DownloadKbps * 1000L / 8 * mult);
                            var bucket = _downloadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                            bucket.Rate = rate;
                            if (!bucket.Consume(packet.Length)) { dropped = true; break; }
                        }
                    }

                    if (!dropped)
                        await _divert.SendAsync(packet, addr);
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception e) when (ct.IsCancellationRequested) { }
            catch (Exception e) { Logger.Error(e, "ConsoleModule"); }
        }

        private static bool MatchesGamePorts(ReadOnlySpan<byte> data, List<int> ports)
        {
            if (data.Length < 20) return false;
            int ipHdrLen = (data[0] & 0x0F) * 4;
            byte protocol = data[9];
            if (protocol != 17 /* UDP */ || data.Length < ipHdrLen + 4) return false;
            int srcPort = (data[ipHdrLen] << 8) | data[ipHdrLen + 1];
            int dstPort = (data[ipHdrLen + 2] << 8) | data[ipHdrLen + 3];
            return ports.Contains(srcPort) || ports.Contains(dstPort);
        }

        private static bool TryGetIpAddresses(ReadOnlySpan<byte> data, out string src, out string dst)
        {
            src = dst = string.Empty;
            if (data.Length < 20) return false;
            if ((data[0] >> 4) != 4) return false;
            src = $"{data[12]}.{data[13]}.{data[14]}.{data[15]}";
            dst = $"{data[16]}.{data[17]}.{data[18]}.{data[19]}";
            return true;
        }

        public static async Task<List<string>> ScanNetworkAsync(IProgress<int>? progress = null)
        {
            var results = new List<string>();
            var localIp = GetLocalIP();
            if (localIp == null) return results;

            var parts = localIp.Split('.');
            if (parts.Length != 4) return results;
            var subnet = $"{parts[0]}.{parts[1]}.{parts[2]}";

            var tasks = new List<Task>();
            int done = 0;
            var lockObj = new object();

            for (int i = 1; i <= 254; i++)
            {
                var ip = $"{subnet}.{i}";
                if (ip == localIp) continue;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 400);
                        if (reply.Status == IPStatus.Success)
                            lock (lockObj) results.Add(ip);
                    }
                    catch { }
                    finally
                    {
                        var pct = Interlocked.Increment(ref done) * 100 / 253;
                        progress?.Report(pct);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            results.Sort((a, b) =>
            {
                var pa = a.Split('.'); var pb = b.Split('.');
                return int.Parse(pa[3]).CompareTo(int.Parse(pb[3]));
            });
            return results;
        }

        private static string? GetLocalIP()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        return ua.Address.ToString();
                }
            }
            return null;
        }
    }

    public class ConsoleDevice
    {
        public string Name { get; set; } = "Console";
        public string? IP { get; set; }
        public bool Enabled { get; set; } = true;
        public int UploadKbps { get; set; }
        public int DownloadKbps { get; set; }
    }

    internal class TokenBucket
    {
        private long _tokens;
        private long _rate;
        private DateTime _lastFill = DateTime.UtcNow;
        private readonly object _lock = new();

        public long Rate { get => _rate; set => _rate = value; }

        public TokenBucket(long bytesPerSecond)
        {
            _rate = bytesPerSecond;
            _tokens = bytesPerSecond;
        }

        public bool Consume(int bytes)
        {
            lock (_lock)
            {
                Refill();
                if (_tokens >= bytes) { _tokens -= bytes; return true; }
                return false;
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastFill).TotalSeconds;
            _lastFill = now;
            _tokens = Math.Min(_rate, _tokens + (long)(elapsed * _rate));
        }
    }

    public class GameProfile
    {
        public string Name { get; }
        public List<int> Ports { get; }

        public GameProfile(string name, List<int> ports)
        {
            Name = name;
            Ports = ports;
        }

        public override string ToString() => Name;
    }
}
