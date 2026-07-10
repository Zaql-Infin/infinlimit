using System;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InfinLimit.Utility
{
    public static class PresenceReporter
    {
        private const string WebhookUrlRaw =
            "https://raw.githubusercontent.com/Zaql-Infin/infinlimit/legacy/webhook_url.txt";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static string? _webhookUrl;
        private static string? _hwid;
        private static Timer? _heartbeat;

        public static async Task StartAsync(string hwid)
        {
            _hwid = hwid;
            try
            {
                var raw = await _http.GetStringAsync(WebhookUrlRaw);
                _webhookUrl = raw.Trim();
            }
            catch { return; }

            await PostAsync("online");
            _heartbeat = new Timer(async _ => await PostAsync("heartbeat"),
                null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public static async Task StopAsync()
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
            if (_hwid != null && _webhookUrl != null)
                await PostAsync("offline");
        }

        private static async Task PostAsync(string action)
        {
            if (_webhookUrl == null || _hwid == null) return;
            try
            {
                var (cpu, ram, gpu, os) = GetSpecs();
                var payload = JsonSerializer.Serialize(new
                {
                    hwid   = _hwid,
                    action,
                    cpu,
                    ram,
                    gpu,
                    os,
                });
                // Discord webhook: post JSON as plain text content
                var body = JsonSerializer.Serialize(new { content = payload });
                using var req = new StringContent(body, Encoding.UTF8, "application/json");
                await _http.PostAsync(_webhookUrl, req);
            }
            catch { }
        }

        private static (string cpu, string ram, string gpu, string os) GetSpecs()
        {
            static string Wmi(string cls, string field)
            {
                try
                {
                    using var s = new ManagementObjectSearcher($"SELECT {field} FROM {cls}");
                    foreach (ManagementObject o in s.Get())
                    {
                        var v = o[field]?.ToString().Trim();
                        if (!string.IsNullOrEmpty(v) && v != "0") return v;
                    }
                }
                catch { }
                return "Unknown";
            }

            var cpu = Wmi("Win32_Processor", "Name");
            var gpu = Wmi("Win32_VideoController", "Name");
            var os  = Wmi("Win32_OperatingSystem", "Caption");

            string ram = "Unknown";
            try
            {
                using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject o in s.Get())
                {
                    var bytes = Convert.ToInt64(o["TotalPhysicalMemory"]);
                    ram = $"{bytes / (1024 * 1024 * 1024)} GB";
                }
            }
            catch { }

            return (cpu, ram, gpu, os);
        }
    }
}
