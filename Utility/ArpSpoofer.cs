using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using SharpPcap;
using SharpPcap.LibPcap;

namespace InfinLimit.Utility
{
    public class ArpSpoofer
    {
        private ILiveDevice? _dev;
        private CancellationTokenSource? _cts;
        private Task? _loop;

        private byte[] _localMac = new byte[6];
        private byte[] _gatewayMac = new byte[6];
        private byte[] _gatewayIpBytes = new byte[4];
        private readonly Dictionary<string, byte[]> _consoleIpBytes = new();
        private readonly Dictionary<string, byte[]> _consoleMacs = new();

        private string _localIp = string.Empty;
        private EventHandler? _processExitHandler;

        public bool IsActive { get; private set; }
        public string StatusMessage { get; private set; } = "Idle";

        // Enables IP forwarding unconditionally — called even when ARP spoofing isn't needed
        // (e.g. PS4 connected through PC hotspot, where traffic flows naturally via ICS).
        public static void EnsureIpForwarding() => EnableIpForwarding(GetLocalIP());

        public bool Start(List<string> consoleIps)
        {
            try
            {
                Logger.Info("ArpSpoofer: starting setup");
                if (!TrySetup(consoleIps)) return false;
                _localIp = GetLocalIP();
                EnableIpForwarding(_localIp);
                _processExitHandler = (_, __) => EmergencyCleanup();
                AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
                // Lock in the correct gateway MAC in the PC's ARP cache so our own
                // spoof replies can't poison the PC's routing (switch flooding risk).
                ProtectPcArp();
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => SpoofLoop(_cts.Token));
                IsActive = true;
                StatusMessage = "ARP active";
                Logger.Info("ArpSpoofer: started successfully");
                return true;
            }
            catch (Exception e)
            {
                StatusMessage = $"ARP error: {e.Message}";
                Logger.Error(e, "ArpSpoofer.Start");
                return false;
            }
        }

        public void Stop()
        {
            IsActive = false;
            StatusMessage = "Idle";
            _cts?.Cancel();

            if (_processExitHandler != null)
            {
                AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
                _processExitHandler = null;
            }

            // Snapshot state, then null _dev so the still-running spoof loop
            // falls into the null-check in SendArpReplyOn and becomes a no-op.
            // That means we can start restoration immediately without waiting
            // for _loop to exit — no more spoof packets can be sent after _dev = null.
            var dev = _dev;
            _dev = null;
            var localIp        = _localIp;
            var consoleMacs    = new Dictionary<string, byte[]>(_consoleMacs);
            var consoleIpBytes = new Dictionary<string, byte[]>(_consoleIpBytes);
            var gwMac = (byte[])_gatewayMac.Clone();
            var gwIp  = (byte[])_gatewayIpBytes.Clone();

            // Fire-and-forget: restore ARP 10× over 1 s, then disable IP forwarding.
            Task.Run(async () =>
            {
                // Remove the static gateway ARP entry so Windows can refresh it dynamically.
                RestorePcArp();

                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        foreach (var (ip, consoleMac) in consoleMacs)
                        {
                            var consoleIpB = consoleIpBytes[ip];
                            // Tell router: console's REAL MAC
                            SendArpReplyOn(dev, gwMac, gwIp, consoleMac, consoleIpB);
                            // Tell console: gateway's REAL MAC
                            SendArpReplyOn(dev, consoleMac, consoleIpB, gwMac, gwIp);
                        }
                    }
                    catch { }

                    if (i < 9) await Task.Delay(100);
                }

                DisableIpForwarding(localIp);
                try { dev?.Close(); } catch { }
            });
        }

        public void UpdateDevices(List<string> consoleIps)
        {
            foreach (var ip in consoleIps)
            {
                if (_consoleMacs.ContainsKey(ip)) continue;
                var mac = ResolveMAC(ip);
                if (mac != null)
                {
                    _consoleMacs[ip] = mac;
                    _consoleIpBytes[ip] = IPAddress.Parse(ip).GetAddressBytes();
                    Logger.Info($"ArpSpoofer: resolved {ip} → {MacToString(mac)}");
                }
                else
                {
                    Logger.Warning($"ArpSpoofer: could not resolve MAC for {ip}");
                }
            }
        }

        // ── Setup ────────────────────────────────────────────────────────────

        private bool TrySetup(List<string> consoleIps)
        {
            var localIp = GetLocalIP();
            Logger.Info($"ArpSpoofer: local IP = {localIp}");
            if (localIp == null) { StatusMessage = "ARP failed: no local IP"; return false; }

            var gateway = GetDefaultGateway();
            Logger.Info($"ArpSpoofer: gateway = {gateway}");
            if (gateway == null) { StatusMessage = "ARP failed: no gateway"; return false; }
            _gatewayIpBytes = gateway.GetAddressBytes();

            // Get local NIC MAC
            var localNic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetIPProperties().UnicastAddresses.Any(a =>
                    a.Address.AddressFamily == AddressFamily.InterNetwork
                    && a.Address.ToString() == localIp));

            if (localNic == null) { StatusMessage = "ARP failed: no NIC found"; return false; }
            _localMac = localNic.GetPhysicalAddress().GetAddressBytes();
            Logger.Info($"ArpSpoofer: local MAC = {MacToString(_localMac)}");

            // Resolve gateway MAC
            var gwMac = ResolveMAC(gateway.ToString());
            Logger.Info($"ArpSpoofer: gateway MAC = {(gwMac == null ? "null" : MacToString(gwMac))}");
            if (gwMac == null) { StatusMessage = "ARP failed: can't resolve gateway MAC"; return false; }
            _gatewayMac = gwMac;

            // Resolve console MACs
            _consoleMacs.Clear();
            _consoleIpBytes.Clear();
            foreach (var ip in consoleIps)
            {
                var mac = ResolveMAC(ip);
                if (mac != null)
                {
                    _consoleMacs[ip] = mac;
                    _consoleIpBytes[ip] = IPAddress.Parse(ip).GetAddressBytes();
                    Logger.Info($"ArpSpoofer: console {ip} → {MacToString(mac)}");
                }
                else
                {
                    Logger.Warning($"ArpSpoofer: can't resolve MAC for console {ip} — is it online?");
                }
            }

            // Find SharpPcap device — try by IP first, fall back to first active non-loopback
            _dev = null;
            var allDevices = CaptureDeviceList.Instance;
            Logger.Info($"ArpSpoofer: {allDevices.Count} pcap devices found");

            foreach (var d in allDevices)
            {
                if (d is LibPcapLiveDevice ld)
                {
                    Logger.Info($"ArpSpoofer: device '{ld.Name}' ({ld.Description})");
                    foreach (var a in ld.Addresses)
                    {
                        var ip = a.Addr?.ipAddress?.ToString();
                        Logger.Info($"  addr: {ip}");
                        if (ip == localIp) { _dev = ld; break; }
                    }
                    if (_dev != null) break;
                }
            }

            // Fallback: pick first device whose description contains the NIC's description
            if (_dev == null)
            {
                Logger.Warning("ArpSpoofer: no device matched by IP, trying by NIC description");
                foreach (var d in allDevices)
                {
                    if (d is LibPcapLiveDevice ld
                        && ld.Description != null
                        && localNic.Description != null
                        && ld.Description.Contains(localNic.Description.Split('(')[0].Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _dev = ld;
                        Logger.Info($"ArpSpoofer: matched by description: {ld.Description}");
                        break;
                    }
                }
            }

            // Last resort: first non-loopback device
            if (_dev == null)
            {
                Logger.Warning("ArpSpoofer: falling back to first non-loopback device");
                _dev = allDevices.OfType<LibPcapLiveDevice>()
                    .FirstOrDefault(d => !d.Name.Contains("loopback", StringComparison.OrdinalIgnoreCase)
                                      && !d.Description.Contains("loopback", StringComparison.OrdinalIgnoreCase));
            }

            if (_dev == null) { StatusMessage = "ARP failed: no pcap device"; return false; }

            Logger.Info($"ArpSpoofer: using device '{_dev.Name}'");
            _dev.Open(DeviceModes.Promiscuous, 1000);
            return true;
        }

        // ── Spoof loop ───────────────────────────────────────────────────────

        private async Task SpoofLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (var (ip, consoleMac) in _consoleMacs)
                    {
                        var consoleIpB = _consoleIpBytes[ip];
                        // Tell router: "console IP lives at PC's MAC"
                        SendArpReply(_gatewayMac, _gatewayIpBytes, _localMac, consoleIpB);
                        // Tell console: "gateway IP lives at PC's MAC"
                        SendArpReply(consoleMac,  consoleIpB,      _localMac, _gatewayIpBytes);
                    }
                }
                catch (Exception e) { Logger.Warning($"ArpSpoofer spoof tick: {e.Message}"); }

                await Task.Delay(1500, ct).ConfigureAwait(false);
            }
        }

        private void RestoreArp()
        {
            if (_dev == null) return;
            foreach (var (ip, consoleMac) in _consoleMacs)
            {
                var consoleIpB = _consoleIpBytes[ip];
                // Restore: tell router console's real MAC
                SendArpReply(_gatewayMac, _gatewayIpBytes, consoleMac,  consoleIpB);
                // Restore: tell console gateway's real MAC
                SendArpReply(consoleMac,  consoleIpB,      _gatewayMac, _gatewayIpBytes);
            }
        }

        // ── PC ARP protection ────────────────────────────────────────────────────

        // Adds a permanent ARP entry for the gateway so our spoof replies can't
        // poison the PC's own routing table if the switch floods unicast frames.
        private void ProtectPcArp()
        {
            var localIp  = GetLocalIP();
            var gwIpStr  = string.Join(".", _gatewayIpBytes);
            var gwMacStr = string.Join("-", _gatewayMac.Select(b => b.ToString("X2")));
            var ifAlias  = GetInterfaceAlias(localIp);

            var cmd = string.IsNullOrEmpty(ifAlias)
                ? $"arp -s {gwIpStr} {gwMacStr}"
                : $"$e = Get-NetNeighbor -IPAddress '{gwIpStr}' -InterfaceAlias '{ifAlias}' -ErrorAction SilentlyContinue; " +
                  $"if ($e) {{ Set-NetNeighbor -IPAddress '{gwIpStr}' -InterfaceAlias '{ifAlias}' -LinkLayerAddress '{gwMacStr}' -State Permanent }} " +
                  $"else {{ New-NetNeighbor -IPAddress '{gwIpStr}' -InterfaceAlias '{ifAlias}' -LinkLayerAddress '{gwMacStr}' -State Permanent }}";

            RunPowerShell(cmd);
            Logger.Info($"ArpSpoofer: PC ARP locked: {gwIpStr} -> {gwMacStr}");
        }

        // Removes the static entry so Windows can refresh the gateway ARP dynamically.
        private void RestorePcArp()
        {
            var localIp = GetLocalIP();
            var gwIpStr = string.Join(".", _gatewayIpBytes);
            var ifAlias = GetInterfaceAlias(localIp);

            var cmd = string.IsNullOrEmpty(ifAlias)
                ? $"arp -d {gwIpStr}"
                : $"Remove-NetNeighbor -IPAddress '{gwIpStr}' -InterfaceAlias '{ifAlias}' -Confirm:$false -ErrorAction SilentlyContinue";

            RunPowerShell(cmd);
            Logger.Info($"ArpSpoofer: PC ARP unlocked: {gwIpStr}");
        }

        private static void RunPowerShell(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NonInteractive -WindowStyle Hidden -Command \"{cmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(psi)?.WaitForExit(5000);
            }
            catch (Exception e) { Logger.Warning($"ArpSpoofer PowerShell: {e.Message}"); }
        }

        // ── ARP frame ────────────────────────────────────────────────────────

        private void SendArpReply(byte[] dstMac, byte[] dstIp, byte[] spoofedMac, byte[] spoofedIp)
            => SendArpReplyOn(_dev, dstMac, dstIp, spoofedMac, spoofedIp);

        private static void SendArpReplyOn(ILiveDevice? dev, byte[] dstMac, byte[] dstIp, byte[] spoofedMac, byte[] spoofedIp)
        {
            if (dev == null) return;
            var frame = new byte[42];
            Array.Copy(dstMac,     0, frame, 0,  6);
            Array.Copy(spoofedMac, 0, frame, 6,  6);
            frame[12] = 0x08; frame[13] = 0x06;
            frame[14] = 0x00; frame[15] = 0x01;
            frame[16] = 0x08; frame[17] = 0x00;
            frame[18] = 6; frame[19] = 4;
            frame[20] = 0x00; frame[21] = 0x02;
            Array.Copy(spoofedMac, 0, frame, 22, 6);
            Array.Copy(spoofedIp,  0, frame, 28, 4);
            Array.Copy(dstMac,     0, frame, 32, 6);
            Array.Copy(dstIp,      0, frame, 38, 4); // target IP is at offset 38, not 36
            dev.SendPacket(frame);
        }

        // ── IP Forwarding ────────────────────────────────────────────────────

        private static void EnableIpForwarding(string localIp)
        {
            try
            {
                // Enable forwarding only on the interface that has localIp,
                // so the PC's other interfaces are unaffected.
                var ifAlias = GetInterfaceAlias(localIp);
                var cmd = string.IsNullOrEmpty(ifAlias)
                    ? "Get-NetIPInterface -AddressFamily IPv4 | Set-NetIPInterface -Forwarding Enabled"
                    : $"Set-NetIPInterface -InterfaceAlias '{ifAlias}' -AddressFamily IPv4 -Forwarding Enabled";

                Logger.Info($"ArpSpoofer: enabling IP forwarding on '{ifAlias ?? "all"}'");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NonInteractive -WindowStyle Hidden -Command \"{cmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = Process.Start(psi);
                p?.WaitForExit(8000);
                Logger.Info($"ArpSpoofer: IP forwarding exit code = {p?.ExitCode}");
            }
            catch (Exception e)
            {
                Logger.Warning($"ArpSpoofer: IP forwarding failed: {e.Message}");
            }
        }

        private static void DisableIpForwarding(string localIp)
        {
            try
            {
                var ifAlias = GetInterfaceAlias(localIp);
                var cmd = string.IsNullOrEmpty(ifAlias)
                    ? "Get-NetIPInterface -AddressFamily IPv4 | Set-NetIPInterface -Forwarding Disabled"
                    : $"Set-NetIPInterface -InterfaceAlias '{ifAlias}' -AddressFamily IPv4 -Forwarding Disabled";

                Logger.Info($"ArpSpoofer: disabling IP forwarding on '{ifAlias ?? "all"}'");
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NonInteractive -WindowStyle Hidden -Command \"{cmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = Process.Start(psi);
                p?.WaitForExit(8000);
                Logger.Info($"ArpSpoofer: IP forwarding disabled, exit code = {p?.ExitCode}");
            }
            catch (Exception e)
            {
                Logger.Warning($"ArpSpoofer: disable IP forwarding failed: {e.Message}");
            }
        }

        // Synchronous cleanup for process exit / crash — no async, must complete before the OS tears down the process.
        private void EmergencyCleanup()
        {
            if (!IsActive) return;
            IsActive = false;
            _cts?.Cancel();
            var dev = _dev;
            _dev = null;

            try { RestorePcArp(); } catch { }

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    foreach (var (ip, consoleMac) in _consoleMacs)
                    {
                        var consoleIpB = _consoleIpBytes[ip];
                        SendArpReplyOn(dev, _gatewayMac, _gatewayIpBytes, consoleMac, consoleIpB);
                        SendArpReplyOn(dev, consoleMac, consoleIpB, _gatewayMac, _gatewayIpBytes);
                    }
                }
                catch { }
                if (i < 2) Thread.Sleep(100);
            }

            DisableIpForwarding(_localIp);
            try { dev?.Close(); } catch { }
        }

        private static string? GetInterfaceAlias(string localIp)
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork
                        && ua.Address.ToString() == localIp)
                        return ni.Name;
            }
            return null;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static byte[]? ResolveMAC(string ip)
        {
            try
            {
                var addrBytes = IPAddress.Parse(ip).GetAddressBytes();
                uint dest = BitConverter.ToUInt32(addrBytes, 0);
                uint src = 0;
                byte[] mac = new byte[6];
                uint len = 6;
                int result = SendARP(dest, src, mac, ref len);
                return result == 0 ? mac : null;
            }
            catch { return null; }
        }

        private static string GetLocalIP()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        return ua.Address.ToString();
            }
            return string.Empty;
        }

        private static IPAddress? GetDefaultGateway()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork
                        && !gw.Address.ToString().StartsWith("169."))
                        return gw.Address;
            }
            return null;
        }

        private static string MacToString(byte[] mac) =>
            string.Join(":", mac.Select(b => b.ToString("X2")));

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] pMacAddr, ref uint phyAddrLen);
    }
}
