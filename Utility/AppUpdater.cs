using System;
using System.Threading.Tasks;
using System.Windows;

using Velopack;
using Velopack.Sources;

namespace InfinLimit.Utility
{
    public static class AppUpdater
    {
        private const string ReleasesRepoUrl = "https://github.com/Zaql-Infin/infinlimit-releases";

        public static async Task CheckForUpdatesAsync()
        {
            if (string.IsNullOrEmpty(ReleasesRepoUrl))
                return;

            try
            {
                var mgr = new UpdateManager(new GithubSource(ReleasesRepoUrl, null, false));

                if (!mgr.IsInstalled)
                    return;

                var updateInfo = await mgr.CheckForUpdatesAsync();
                if (updateInfo == null)
                    return;

                await mgr.DownloadUpdatesAsync(updateInfo);

                // Apply on the UI thread so WPF shuts down cleanly before the new process launches
                Application.Current.Dispatcher.Invoke(() => mgr.ApplyUpdatesAndRestart(updateInfo));
            }
            catch
            {
                // Never crash the app over a failed update check
            }
        }
    }
}
