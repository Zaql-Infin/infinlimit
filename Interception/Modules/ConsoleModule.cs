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
    /// <summary>
    /// Standalone bandwidth throttler for console devices on the LAN.
    /// Uses token-bucket algorithm via WinDivert packet interception.
    /// </summary>
    public class ConsoleModule
    {
        public static List<ConsoleDevice> Devices { get; } = new();
        public bool IsEnabled { get; private set; }

        private readonly ConcurrentDictionary<string, TokenBucket> _uploadBuckets = new();
        private readonly ConcurrentDictionary<string, TokenBucket> _downloadBuckets = new();
        private WinDivert? _divert;
        private CancellationTokenSource? _cts;

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

                    bool dropped = false;

                    foreach (var device in Devices)
                    {
                        if (!device.Enabled || device.IP == null) continue;

                        // Traffic FROM the device = its upload
                        if (srcIp == device.IP && device.UploadKbps > 0)
                        {
                            var bucket = _uploadBuckets.GetOrAdd(device.IP,
                                _ => new TokenBucket(device.UploadKbps * 1000L / 8));
                            bucket.Rate = device.UploadKbps * 1000L / 8;
                            if (!bucket.Consume(packet.Length)) { dropped = true; break; }
                        }

                        // Traffic TO the device = its download
                        if (dstIp == device.IP && device.DownloadKbps > 0)
                        {
                            var bucket = _downloadBuckets.GetOrAdd(device.IP,
                                _ => new TokenBucket(device.DownloadKbps * 1000L / 8));
                            bucket.Rate = device.DownloadKbps * 1000L / 8;
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
}
