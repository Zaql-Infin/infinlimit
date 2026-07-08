using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Readers;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

using ZipArchive = SharpCompress.Archives.Zip.ZipArchive;

namespace InfinLimit.Utility
{
    public static class Updater
    {
        public const int Version = 1;
        public const string VersionString = "2.0.0";

        // TODO: Set this to your manifest URL when you're ready
        private const string ManifestUrl = "";

        private static Manifest _manifest;
        private static readonly HttpClient _http = new();

        public static async Task<bool> IsLatestAsync()
        {
            if (string.IsNullOrEmpty(ManifestUrl))
                return true;

            try
            {
                var json = await _http.GetStringAsync(ManifestUrl);
                if (json.Length < 5) return true;
                _manifest = JsonSerializer.Deserialize<Manifest>(json);
                return _manifest == null || _manifest.Version <= Version;
            }
            catch (Exception e)
            {
                ExtraLogger.Error(e);
                return true;
            }
        }

        public static bool IsLatest()
        {
            if (string.IsNullOrEmpty(ManifestUrl))
                return true;

            try
            {
                using var wc = new WebClient();
                var json = wc.DownloadString(ManifestUrl);
                if (json.Length < 5) return true;
                _manifest = JsonSerializer.Deserialize<Manifest>(json);
                return _manifest == null || _manifest.Version <= Version;
            }
            catch (Exception e)
            {
                ExtraLogger.Error(e);
                return true;
            }
        }

        public static async Task<bool> UpdateAsync(IProgress<int> progress = null)
        {
            if (_manifest == null) return false;

            var tempZip = Path.Combine(Path.GetTempPath(), "infinlimit_update.zip");
            var tempDir = Path.Combine(Path.GetTempPath(), "infinlimit_update");

            try
            {
                using var wc = new WebClient();
                if (progress != null)
                    wc.DownloadProgressChanged += (s, e) => progress.Report(e.ProgressPercentage);

                await wc.DownloadFileTaskAsync(_manifest.DownloadUrl, tempZip);
                Directory.CreateDirectory(tempDir);

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, tempDir, true);
                }
                catch (InvalidDataException)
                {
                    using var fs = File.OpenRead(tempZip);
                    using var zip = ZipArchive.Open(fs);
                    zip.WriteToDirectory(tempDir, new SharpCompress.Common.ExtractionOptions()
                    {
                        Overwrite = true,
                        ExtractFullPath = true
                    });
                }

                LaunchPatcher(tempDir);
                return true;
            }
            catch (Exception e)
            {
                ExtraLogger.Error(e);
                return false;
            }
        }

        // Legacy sync API kept for StartupProgressBar compatibility
        public static bool Update() => UpdateAsync().GetAwaiter().GetResult();
        public static bool IsProtected() => false;
        public static void RunPatcher(string args = null) { }

        private static void LaunchPatcher(string updateDir)
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            var patcherScript = Path.Combine(Path.GetTempPath(), "infinlimit_patch.bat");

            File.WriteAllText(patcherScript,
                $"@echo off\r\n" +
                $"timeout /t 2 /nobreak >nul\r\n" +
                $"xcopy /s /y \"{updateDir}\\*\" \"{Path.GetDirectoryName(exePath)}\\\"\r\n" +
                $"start \"\" \"{exePath}\"\r\n" +
                $"del \"{patcherScript}\"\r\n"
            );

            Process.Start(new ProcessStartInfo
            {
                FileName = patcherScript,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    public class Manifest
    {
        public int Version { get; set; }
        public string VersionString { get; set; }
        public string DownloadUrl { get; set; }
        public string MD5 { get; set; }
        public string ChangeLog { get; set; }
    }
}
