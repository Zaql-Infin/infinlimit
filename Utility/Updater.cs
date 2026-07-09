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
        // ── Fill these in before distributing ──────────────────────────────────
        //   1. Push a GitHub release tagged "v{int}" (e.g. v3, v4, v5 …)
        //   2. Attach InfinLimit.exe as a release asset — that's it.
        //   3. Bump Version here each time you build so existing clients know
        //      a newer build is available.
        public const int Version = 14;
        public const string VersionString = "14.0.0";

        public const string RepoOwner = "Zaql-Infin";
        public const string RepoName  = "infinlimit";
        // ───────────────────────────────────────────────────────────────────────

        private static readonly HttpClient _http = new();

        static Updater()
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("InfinLimit", VersionString));
        }

        private static string _pendingDownloadUrl;

        /// <summary>Returns true if a newer release exists on GitHub.</summary>
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

                // tag like "v3" or "v2.1.0" — compare leading integer only
                var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
                var part = tag.Split('.')[0];
                if (!int.TryParse(part, out int remote) || remote <= Version)
                    return false;

                // find "InfinLimitSetup.exe" in the release assets
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals("InfinLimitSetup.exe", StringComparison.OrdinalIgnoreCase))
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

        /// <summary>
        /// Downloads the update exe then hands off to the patcher bat and exits.
        /// </summary>
        public static async Task<bool> DownloadAndApplyAsync(IProgress<int> progress = null)
        {
            if (string.IsNullOrEmpty(_pendingDownloadUrl))
                return false;

            var tempExe = Path.Combine(Path.GetTempPath(), "InfinLimitSetup_update.exe");

            try
            {
                using var resp = await _http.GetAsync(
                    _pendingDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                var total = resp.Content.Headers.ContentLength ?? 0L;
                var buf   = new byte[81920];
                long done = 0;

                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(tempExe);

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
                return false;
            }

            LaunchPatcher(tempExe);
            return true;
        }

        private static void LaunchPatcher(string newSetup)
        {
            // We shut ourselves down, so /RESTARTAPPLICATIONS never fires (the installer
            // only restarts apps it closed itself). Instead, write a bat that:
            //   1. Waits for this process to fully exit
            //   2. Runs the installer silently
            //   3. Relaunches the exe from the same path it was running at
            var current = Process.GetCurrentProcess().MainModule!.FileName;
            var bat     = Path.Combine(Path.GetTempPath(), "infinlimit_update.bat");

            File.WriteAllText(bat,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"\"{newSetup}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART\r\n" +
                $"start \"\" \"{current}\"\r\n" +
                $"del \"{newSetup}\"\r\n" +
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

        // Legacy stubs kept for StartupProgressBar
        public static bool IsProtected()  => true;
        public static void RunPatcher(string _ = null) { }
        public static bool Update()       => false;
        public static bool IsLatest()     => true;
    }
}
