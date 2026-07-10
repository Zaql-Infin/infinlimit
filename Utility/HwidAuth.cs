using System;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace InfinLimit.Utility
{
    public static class HwidAuth
    {
        private const string BlacklistBase = "https://raw.githubusercontent.com/Zaql-Infin/infinlimit/legacy/blacklist.txt";
        private const string WhitelistBase = "https://raw.githubusercontent.com/Zaql-Infin/infinlimit/legacy/whitelist.txt";
        private const int    RecheckMins  = 1;

        // Append a timestamp so GitHub's CDN never serves a stale cached copy
        private static string BlacklistUrl => $"{BlacklistBase}?_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        private static string WhitelistUrl => $"{WhitelistBase}?_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static string? _hwid;
        private static CancellationTokenSource? _bgCts;

        public static string Hwid => _hwid ??= BuildHwid();

        public enum AuthResult { Allowed, NotWhitelisted, Blacklisted, NetworkError }

        public static async Task<AuthResult> CheckAsync()
        {
            try
            {
                var hwid = Hwid;

                // 1. Check blacklist (if present → banned immediately)
                try
                {
                    var bl = await _http.GetStringAsync(BlacklistUrl);
                    foreach (var line in bl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!line.StartsWith('#') && line.Equals(hwid, StringComparison.OrdinalIgnoreCase))
                            return AuthResult.Blacklisted;
                }
                catch { /* blacklist fetch failure is non-fatal */ }

                // 2. Check whitelist (must be present → allowed)
                var wl = await _http.GetStringAsync(WhitelistUrl);
                foreach (var line in wl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!line.StartsWith('#') && line.Equals(hwid, StringComparison.OrdinalIgnoreCase))
                        return AuthResult.Allowed;

                return AuthResult.NotWhitelisted;
            }
            catch (Exception ex)
            {
                ExtraLogger.Error(ex);
                return AuthResult.NetworkError;
            }
        }

        public static void StartBackgroundCheck()
        {
            _bgCts = new CancellationTokenSource();
            var ct = _bgCts.Token;
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(RecheckMins), ct); }
                    catch (TaskCanceledException) { break; }

                    var result = await CheckAsync();
                    if (result is AuthResult.Blacklisted or AuthResult.NotWhitelisted)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var title = result == AuthResult.Blacklisted ? "Access Denied" : "Access Revoked";
                            var msg   = result == AuthResult.Blacklisted
                                ? "Your PC has been blacklisted from InfinLimiter.\nContact support if you believe this is an error."
                                : "Your access to InfinLimiter has been revoked.\nContact support to get re-whitelisted.";
                            new InfinLimit.AuthDialog(title, msg, Hwid).ShowDialog();
                            Application.Current.Shutdown(1);
                        });
                        return;
                    }
                }
            }, ct);
        }

        public static void StopBackgroundCheck() { _bgCts?.Cancel(); _bgCts = null; }

        private static string BuildHwid()
        {
            static string Wmi(string cls, string field)
            {
                try
                {
                    using var s = new ManagementObjectSearcher($"SELECT {field} FROM {cls}");
                    foreach (ManagementObject o in s.Get())
                    {
                        var v = o[field]?.ToString().Trim();
                        if (!string.IsNullOrEmpty(v) && v != "0" && v != "None") return v;
                    }
                }
                catch { }
                return "?";
            }

            var raw = string.Join("|",
                Wmi("Win32_Processor",      "ProcessorId"),
                Wmi("Win32_BaseBoard",       "SerialNumber"),
                Wmi("Win32_OperatingSystem", "SerialNumber"),
                Wmi("Win32_DiskDrive",       "SerialNumber"));

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLower()[..32];
        }
    }
}
