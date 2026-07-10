using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace InfinLimit.Utility
{
    public static class Updater
    {
        public const int    Version       = 83;
        public const string VersionString = "83.0.0";

        public const string RepoOwner = "Zaql-Infin";
        public const string RepoName  = "infinlimit";

        private static readonly HttpClient _http = new();

        static Updater()
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("InfinLimit", VersionString));
        }

        private static string? _pendingDownloadUrl;

        public static async Task<bool> IsUpdateAvailableAsync()
        {
            if (string.IsNullOrEmpty(RepoOwner) || string.IsNullOrEmpty(RepoName))
                return false;

            try
            {
                var url  = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var json = await _http.GetStringAsync(url);
                using var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tag  = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
                var part = tag.Split('.')[0];
                if (!int.TryParse(part, out int remote) || remote <= Version)
                    return false;

                // Look for the raw InfinLimit.exe asset (no installer needed)
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals("InfinLimit.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingDownloadUrl = asset.GetProperty("browser_download_url").GetString();
                        return !string.IsNullOrEmpty(_pendingDownloadUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                ExtraLogger.Error(ex);
            }

            return false;
        }

        public static async Task<bool> DownloadAndApplyAsync(IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(_pendingDownloadUrl))
                return false;

            // Download the new exe next to the current one so it stays on the same drive
            var current    = Process.GetCurrentProcess().MainModule!.FileName;
            var newExeTemp = current + ".new";

            try
            {
                using var resp = await _http.GetAsync(
                    _pendingDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                var total = resp.Content.Headers.ContentLength ?? 0L;
                var buf   = new byte[81920];
                long done = 0;

                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(newExeTemp);

                int read;
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read));
                    done += read;
                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
            }
            catch (Exception ex)
            {
                ExtraLogger.Error(ex);
                try { File.Delete(newExeTemp); } catch { }
                return false;
            }

            LaunchPatcher(current, newExeTemp);
            return true;
        }

        private static void LaunchPatcher(string currentExe, string newExeTemp)
        {
            var bat = Path.Combine(Path.GetTempPath(), "infinlimit_update.bat");

            // The patcher:
            //  1. Kills InfinLimit by name + by PID (belt and suspenders)
            //  2. Waits until the process is fully gone
            //  3. Moves the new exe over the old one (same drive = atomic rename)
            //  4. Relaunches from the same path
            var pid = Process.GetCurrentProcess().Id;

            File.WriteAllText(bat,
                "@echo off\r\n" +
                $"taskkill /F /PID {pid} >nul 2>&1\r\n" +
                "taskkill /F /IM InfinLimit.exe >nul 2>&1\r\n" +
                ":wait\r\n" +
                "tasklist /FI \"IMAGENAME eq InfinLimit.exe\" 2>nul | find /I \"InfinLimit.exe\" >nul\r\n" +
                "if not errorlevel 1 (timeout /t 1 /nobreak >nul && goto wait)\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                // Move new exe over old exe (works even on locked files after process exits)
                $"move /Y \"{newExeTemp}\" \"{currentExe}\"\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                "del \"%~f0\"\r\n"
            );

            Process.Start(new ProcessStartInfo
            {
                FileName        = bat,
                UseShellExecute = true,
                WindowStyle     = ProcessWindowStyle.Hidden
            });

            Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
        }

        public static bool IsProtected()  => true;
        public static void RunPatcher(string _ = null) { }
        public static bool Update()       => false;
        public static bool IsLatest()     => true;
    }
}
