using System;
using System.Threading.Tasks;

using Velopack;
using Velopack.Sources;

namespace InfinLimit.Utility
{
    public static class AppUpdater
    {
        // TODO: set this to your releases repo once created
        // e.g. "https://github.com/YourUser/infinlimit-releases"
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
                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch
            {
                // Never crash the app over a failed update check
            }
        }
    }
}
