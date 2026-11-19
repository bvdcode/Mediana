// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli.Updates
{
    internal class SelfUpdater(GitHubReleaseClient releases, UpdateInstaller installer, ILogger logger)
    {
        public async Task UpdateAsync(string executablePath, bool checkOnly, CancellationToken token)
        {
            Version currentVersion = await ExecutableVersion.ReadAsync(executablePath, token);
            logger.LogInformation("Checking the latest GitHub release. Current version: {Version}", currentVersion);
            GitHubRelease release = await releases.GetLatestAsync(token);
            if (release.Version <= currentVersion)
            {
                logger.LogInformation("Mediana {Version} is up to date.", currentVersion);
                return;
            }
            if (checkOnly)
            {
                logger.LogInformation("Mediana {Version} is available. Run mediana update to install it.", release.Version);
                return;
            }

            string directory = Path.GetDirectoryName(executablePath) ?? throw new InvalidOperationException("The executable directory is not available.");
            string stagedPath = Path.Combine(directory, $".{Path.GetFileName(executablePath)}.{Guid.NewGuid():N}.update{Path.GetExtension(executablePath)}");
            bool installationStarted = false;
            try
            {
                logger.LogInformation("Downloading Mediana {Version} ({Size} bytes).", release.Version, release.Size);
                await releases.DownloadAsync(release, stagedPath, token);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(stagedPath, File.GetUnixFileMode(executablePath));
                }
                if (await ExecutableVersion.ReadAsync(stagedPath, token) != release.Version)
                {
                    throw new InvalidDataException("The downloaded executable does not match the release product and version.");
                }

                await installer.InstallAsync(executablePath, stagedPath, release, token);
                installationStarted = true;
                if (OperatingSystem.IsWindows())
                {
                    logger.LogInformation("Verified version {Version} will replace this executable after exit. Update log: {Path}",
                        release.Version, executablePath + ".update.log");
                }
                else
                {
                    logger.LogInformation("Updated successfully to {Version}.", release.Version);
                }
            }
            finally
            {
                if (!installationStarted)
                {
                    File.Delete(stagedPath);
                }
            }
        }
    }
}
