using InfinLimit.Utility;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        public static bool GamePaused = false;

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
        public static List<Keycode> GamePauseKeybind = new();

        private readonly ConcurrentDictionary<string, TokenBucket> _uploadBuckets = new();
        private readonly ConcurrentDictionary<string, TokenBucket> _downloadBuckets = new();
        // Per-device queues: excess packets are delayed here rather than dropped (NetBalancer-style)
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(WinDivertPacket Pkt, WinDivertAddress Addr)>> _downloadQueues = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<(WinDivertPacket Pkt, WinDivertAddress Addr)>> _uploadQueues = new();
        private WinDivert? _divert;
        private CancellationTokenSource? _cts;
        private readonly ArpSpoofer _arpSpoofer = new();
        private volatile bool _passing = false; // when true, forward all packets with no drops

        // Per-device bytes accumulated each second, then published to DLSpeedBps/ULSpeedBps
        private readonly ConcurrentDictionary<string, long> _dlBytesAccum = new();
        private readonly ConcurrentDictionary<string, long> _ulBytesAccum = new();
        public static readonly ConcurrentDictionary<string, long> DLSpeedBps = new();
        public static readonly ConcurrentDictionary<string, long> ULSpeedBps = new();

        // Game filter — null means "All Traffic"
        public static GameProfile? SelectedGame;

        public static readonly List<GameProfile> GameProfiles = new()
        {
            new GameProfile("All Traffic",   new()),
            new GameProfile("Destiny 1",     new() { 3074, 3478, 3479, 3480, 7500, 9308 }),
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
            GamePauseKeybind.AddRange(cfg.GetSettings<List<Keycode>>("GamePauseKeybind") ?? new());

            // Bool flags — check key exists first to apply our own defaults
            DLEnabled = cfg.Settings.ContainsKey("DLEnabled") ? cfg.GetSettings<bool>("DLEnabled") : true;
            ULEnabled = cfg.Settings.ContainsKey("ULEnabled") ? cfg.GetSettings<bool>("ULEnabled") : true;
            AutoResync = cfg.Settings.ContainsKey("AutoResync") ? cfg.GetSettings<bool>("AutoResync") : false;
            Buffering = cfg.Settings.ContainsKey("Buffering") ? cfg.GetSettings<bool>("Buffering") : false;
            GamePaused = cfg.Settings.ContainsKey("GamePaused") ? cfg.GetSettings<bool>("GamePaused") : false;

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
                SaveFlags(); RefreshFilter(); OnStateChanged?.Invoke(); return;
            }
            if (ULKeybind.Count > 0 && MatchesKeybind(keys, ULKeybind))
            {
                ULEnabled = !ULEnabled;
                if (ULEnabled) ULSlowEnabled = false;
                SaveFlags(); RefreshFilter(); OnStateChanged?.Invoke(); return;
            }
            if (DLSlowKeybind.Count > 0 && MatchesKeybind(keys, DLSlowKeybind))
            {
                DLSlowEnabled = !DLSlowEnabled;
                if (DLSlowEnabled) DLEnabled = false;
                SaveFlags(); RefreshFilter(); OnStateChanged?.Invoke(); return;
            }
            if (ULSlowKeybind.Count > 0 && MatchesKeybind(keys, ULSlowKeybind))
            {
                ULSlowEnabled = !ULSlowEnabled;
                if (ULSlowEnabled) ULEnabled = false;
                SaveFlags(); RefreshFilter(); OnStateChanged?.Invoke(); return;
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
            if (GamePauseKeybind.Count > 0 && MatchesKeybind(keys, GamePauseKeybind))
            {
                GamePaused = !GamePaused;
                SaveFlags(); RefreshFilter(); OnStateChanged?.Invoke(); return;
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
            cfg.Settings["GamePaused"] = GamePaused;
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
            cfg.Settings["GamePauseKeybind"] = GamePauseKeybind;
            Config.Save();
        }

        public void Enable()
        {
            if (IsEnabled) return;
            IsEnabled = true;

            // Start WinDivert in pass-all mode FIRST so we're already capturing
            // before ARP spoofing redirects console traffic to us.
            // Previous bug: ARP spoof started before WinDivert was ready → brief
            // packet drop window → Destiny 2 detected disconnect → weasel error.
            _passing = true;
            StartDivert();

            // Give WinDivert ~300ms to open and start receiving, then start ARP.
            // By the time the router's ARP cache updates (~100-200ms after first
            // spoof packet), WinDivert is already forwarding everything it sees.
            Task.Delay(300).ContinueWith(_ =>
            {
                StartArpSpoofer();
                // Wait for ARP to propagate and traffic to stabilise, then throttle.
                Task.Delay(800).ContinueWith(__ => { if (IsEnabled) _passing = false; });
            });
        }

        private void StartArpSpoofer()
        {
            var ips = Devices.Where(d => d.IP != null && d.Enabled).Select(d => d.IP!).ToList();
            if (ips.Count > 0)
            {
                _arpSpoofer.Start(ips);
                OnStateChanged?.Invoke();
            }
        }

        // Called after Devices list is updated so the spoofer picks up new IPs.
        public void RefreshArpDevices()
        {
            if (IsEnabled)
                _arpSpoofer.UpdateDevices(Devices.Where(d => d.IP != null).Select(d => d.IP!).ToList());
        }

        public void Disable()
        {
            if (!IsEnabled) return;
            IsEnabled = false;

            // Pass-all mode immediately so in-flight packets aren't dropped
            // while ARP restoration packets propagate to the router/console.
            _passing = true;
            _downloadQueues.Clear();
            _uploadQueues.Clear();

            var cts = _cts;
            _cts = null;
            _divert = null;

            // Stop ARP spoofing — sends restoration packets to router and console
            // so their ARP caches revert to the real MACs. WinDivert stays in
            // pass-all mode during this window so nothing gets dropped.
            _arpSpoofer.Stop();

            // 500ms gives ARP restoration time to propagate before WinDivert exits.
            Task.Delay(500).ContinueWith(_ =>
            {
                _passing = false;
                cts?.Cancel();
            });
        }

        // Restarts WinDivert with a direction-aware filter based on current DL/UL flags.
        // Called whenever DL, UL, DLSlow, ULSlow, or GamePaused is toggled while active.
        // Downloads no longer matching the filter bypass WinDivert entirely (native IP
        // forwarding, zero added latency) — this prevents the weasel error after unticking DL.
        public void RefreshFilter()
        {
            if (!IsEnabled) return;

            _downloadQueues.Clear();
            _uploadQueues.Clear();

            var oldCts = _cts;
            _cts = null;
            _divert = null;

            oldCts?.Cancel();

            // Brief gap: IP forwarding handles packets natively (no drops, no latency).
            // Then start a new WinDivert with the updated direction-aware filter.
            Task.Delay(20).ContinueWith(_ => StartDivert());
        }

        private void StartDivert()
        {
            var filter = BuildFilter();
            if (filter == null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => ProcessPacketsAsync(_cts.Token));
        }

        private async Task ProcessPacketsAsync(CancellationToken ct)
        {
            var filter = BuildFilter();
            if (filter == null) return;

            WinDivert? divert = null;
            try
            {
                divert = new WinDivert(filter, WinDivertLayer.Forward, priority: 100);
                _divert = divert;

                // Drain task releases queued packets at the configured rate (NetBalancer-style
                // delay instead of drop — game gets every packet, just throttled in time).
                _ = Task.Run(() => DrainQueuesAsync(divert, ct), ct);
                _ = Task.Run(() => SpeedTrackerAsync(ct), ct);

                while (!ct.IsCancellationRequested)
                {
                    var packet = new WinDivertPacket();
                    var addr = new WinDivertAddress();

                    var recvLength = await divert.RecvAsync(packet, addr, ct);

                    // Drain mode: forward received packet before checking cancellation,
                    // so no in-flight packet is dropped during a graceful disable.
                    if (_passing)
                    {
                        await divert.SendAsync(packet, addr);
                        CountPacket(packet.Span, packet.Length);
                        continue;
                    }

                    if (ct.IsCancellationRequested) break;

                    if (!TryGetIpAddresses(packet.Span, out var srcIp, out var dstIp))
                    {
                        await divert.SendAsync(packet, addr);
                        continue;
                    }

                    // Skip packet if it doesn't match the selected game's ports
                    if (SelectedGame != null && SelectedGame.Ports.Count > 0
                        && !MatchesGamePorts(packet.Span, SelectedGame.Ports))
                    {
                        await divert.SendAsync(packet, addr);
                        continue;
                    }

                    bool queued = false;
                    bool dropped = false;
                    int pendingJitter = 0;

                    foreach (var device in Devices)
                    {
                        if (!device.Enabled || device.IP == null) continue;

                        bool isDst = dstIp == device.IP; // download: internet → device
                        bool isSrc = srcIp == device.IP; // upload:   device   → internet
                        if (!isDst && !isSrc) continue;

                        // Global game pause OR per-device Blocked priority → drop
                        if (GamePaused || device.Priority == DevicePriority.Blocked)
                        {
                            dropped = true; break;
                        }

                        // High priority → pass through immediately, no rate limiting
                        if (device.Priority == DevicePriority.High)
                        {
                            pendingJitter = device.JitterMs;
                            break;
                        }

                        // Low priority = 50% of configured rate (yields bandwidth to other traffic)
                        float priorityMult = device.Priority == DevicePriority.Low ? 0.5f : 1.0f;

                        // Upload: traffic FROM device → internet
                        if (isSrc && (ULEnabled || ULSlowEnabled) && device.UploadKbps > 0)
                        {
                            float mult = (ULSlowEnabled ? SlowMultiplier : 1f) * priorityMult;
                            long rate = (long)(device.UploadKbps * 1000L / 8 * mult);
                            var bucket = _uploadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                            bucket.Rate = rate;
                            if (!bucket.Consume(packet.Length))
                            {
                                var q = _uploadQueues.GetOrAdd(device.IP, _ => new ConcurrentQueue<(WinDivertPacket, WinDivertAddress)>());
                                while (q.Count >= 128) q.TryDequeue(out _);
                                q.Enqueue((packet, addr));
                                queued = true; break;
                            }
                        }

                        // Download: traffic from internet → device
                        if (isDst && (DLEnabled || DLSlowEnabled) && device.DownloadKbps > 0)
                        {
                            float mult = (DLSlowEnabled ? SlowMultiplier : 1f) * priorityMult;
                            long rate = (long)(device.DownloadKbps * 1000L / 8 * mult);
                            var bucket = _downloadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                            bucket.Rate = rate;
                            if (!bucket.Consume(packet.Length))
                            {
                                var q = _downloadQueues.GetOrAdd(device.IP, _ => new ConcurrentQueue<(WinDivertPacket, WinDivertAddress)>());
                                while (q.Count >= 128) q.TryDequeue(out _);
                                q.Enqueue((packet, addr));
                                queued = true; break;
                            }
                        }

                        pendingJitter = device.JitterMs;
                    }

                    if (!dropped && !queued)
                    {
                        if (pendingJitter > 0)
                            await Task.Delay(Random.Shared.Next(1, pendingJitter + 1), ct).ConfigureAwait(false);
                        await divert.SendAsync(packet, addr);
                        CountPacket(packet.Span, packet.Length);
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception e) when (ct.IsCancellationRequested) { }
            catch (Exception e) { Logger.Error(e, "ConsoleModule"); }
            finally
            {
                if (ReferenceEquals(_divert, divert)) _divert = null;
                try { divert?.Dispose(); } catch { }
            }
        }

        // Releases queued packets at the configured rate — this is the NetBalancer-style
        // "delay not drop" behaviour. Runs as a companion to ProcessPacketsAsync.
        private async Task DrainQueuesAsync(WinDivert divert, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // In pass-all mode flush everything immediately (graceful disable)
                if (_passing)
                {
                    foreach (var kvp in _downloadQueues)
                        while (kvp.Value.TryDequeue(out var item))
                            try { await divert.SendAsync(item.Pkt, item.Addr); CountDLBytes(kvp.Key, item.Pkt.Length); } catch { }
                    foreach (var kvp in _uploadQueues)
                        while (kvp.Value.TryDequeue(out var item))
                            try { await divert.SendAsync(item.Pkt, item.Addr); CountULBytes(kvp.Key, item.Pkt.Length); } catch { }
                    await Task.Delay(1, ct).ConfigureAwait(false);
                    continue;
                }

                bool sentAny = false;

                foreach (var device in Devices)
                {
                    if (!device.Enabled || device.IP == null) continue;
                    if (device.Priority == DevicePriority.Blocked) continue;

                    float priorityMult = device.Priority == DevicePriority.Low ? 0.5f : 1.0f;

                    if ((DLEnabled || DLSlowEnabled) && device.DownloadKbps > 0
                        && _downloadQueues.TryGetValue(device.IP, out var dlQ) && !dlQ.IsEmpty)
                    {
                        float mult = (DLSlowEnabled ? SlowMultiplier : 1f) * priorityMult;
                        long rate = (long)(device.DownloadKbps * 1000L / 8 * mult);
                        var bucket = _downloadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                        bucket.Rate = rate;

                        while (dlQ.TryPeek(out var peeked) && bucket.Consume(peeked.Pkt.Length))
                        {
                            if (dlQ.TryDequeue(out var item))
                            {
                                if (device.JitterMs > 0)
                                    await Task.Delay(Random.Shared.Next(1, device.JitterMs + 1), ct).ConfigureAwait(false);
                                await divert.SendAsync(item.Pkt, item.Addr);
                                CountDLBytes(device.IP, item.Pkt.Length);
                                sentAny = true;
                            }
                        }
                    }

                    if ((ULEnabled || ULSlowEnabled) && device.UploadKbps > 0
                        && _uploadQueues.TryGetValue(device.IP, out var ulQ) && !ulQ.IsEmpty)
                    {
                        float mult = (ULSlowEnabled ? SlowMultiplier : 1f) * priorityMult;
                        long rate = (long)(device.UploadKbps * 1000L / 8 * mult);
                        var bucket = _uploadBuckets.GetOrAdd(device.IP, _ => new TokenBucket(rate));
                        bucket.Rate = rate;

                        while (ulQ.TryPeek(out var peeked) && bucket.Consume(peeked.Pkt.Length))
                        {
                            if (ulQ.TryDequeue(out var item))
                            {
                                if (device.JitterMs > 0)
                                    await Task.Delay(Random.Shared.Next(1, device.JitterMs + 1), ct).ConfigureAwait(false);
                                await divert.SendAsync(item.Pkt, item.Addr);
                                CountULBytes(device.IP, item.Pkt.Length);
                                sentAny = true;
                            }
                        }
                    }
                }

                if (!sentAny)
                    await Task.Delay(1, ct).ConfigureAwait(false);
            }
        }

        private void CountPacket(ReadOnlySpan<byte> data, int bytes)
        {
            if (!TryGetIpAddresses(data, out var src, out var dst)) return;
            foreach (var d in Devices)
            {
                if (d.IP == null) continue;
                if (d.IP == dst) { CountDLBytes(d.IP, bytes); return; }
                if (d.IP == src) { CountULBytes(d.IP, bytes); return; }
            }
        }

        private void CountDLBytes(string ip, int bytes)
            => _dlBytesAccum.AddOrUpdate(ip, bytes, (_, v) => v + bytes);

        private void CountULBytes(string ip, int bytes)
            => _ulBytesAccum.AddOrUpdate(ip, bytes, (_, v) => v + bytes);

        private async Task SpeedTrackerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch { return; }
                foreach (var key in _dlBytesAccum.Keys)
                    if (_dlBytesAccum.TryRemove(key, out var b)) DLSpeedBps[key] = b;
                foreach (var key in _ulBytesAccum.Keys)
                    if (_ulBytesAccum.TryRemove(key, out var b)) ULSpeedBps[key] = b;
            }
        }

        // Returns null when nothing needs intercepting (all flags off) so WinDivert
        // stays stopped and console traffic flows via native IP forwarding only.
        // When only UL is active, the filter only matches uploads (SrcAddr == console)
        // so downloads bypass WinDivert entirely — no added kernel↔user latency.
        private static string? BuildFilter()
        {
            var enabled = Devices.Where(d => d.IP != null && d.Enabled).ToList();
            if (enabled.Count == 0) return null;

            bool needsDL = DLEnabled || DLSlowEnabled;
            bool needsUL = ULEnabled || ULSlowEnabled;

            if (!needsDL && !needsUL && !GamePaused) return null;

            var ipClauses = new List<string>();

            // GamePaused must intercept both directions to drop all game traffic.
            if (needsDL || GamePaused)
                ipClauses.AddRange(enabled.Select(d => $"ip.DstAddr == {d.IP}"));
            if (needsUL || GamePaused)
                ipClauses.AddRange(enabled.Select(d => $"ip.SrcAddr == {d.IP}"));

            var ipPart = "(" + string.Join(" or ", ipClauses) + ")";

            // Kernel-level port filter: when a specific game is selected, only
            // intercept that game's UDP ports so non-game console traffic
            // (Netflix, Xbox Live, system updates) is never touched.
            if (SelectedGame != null && SelectedGame.Ports.Count > 0)
            {
                var portClauses = SelectedGame.Ports.SelectMany(p => new[]
                {
                    $"udp.SrcPort == {p}",
                    $"udp.DstPort == {p}"
                });
                var portPart = "(" + string.Join(" or ", portClauses) + ")";
                return $"ip and {ipPart} and {portPart}";
            }

            return $"ip and {ipPart}";
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

    public enum DevicePriority { High, Normal, Low, Blocked }

    public class ConsoleDevice
    {
        public string Name { get; set; } = "Console";
        public string? IP { get; set; }
        public bool Enabled { get; set; } = true;
        public int UploadKbps { get; set; }
        public int DownloadKbps { get; set; }
        // Priority: High = no limit; Normal = token-bucket; Low = 50% rate; Blocked = drop all
        public DevicePriority Priority { get; set; } = DevicePriority.Normal;
        // Jitter: random 0–N ms added per packet to simulate link variability
        public int JitterMs { get; set; } = 0;
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
