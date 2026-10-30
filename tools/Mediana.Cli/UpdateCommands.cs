// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using ConsoleAppFramework;
using Mediana.Cli.Updates;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class UpdateCommands(ILogger logger)
    {
        /// <summary>Check for or install the latest stable Mediana executable.</summary>
        /// <param name="check">Check the latest version without downloading or installing it.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [Command("update|--update|-u")]
        public async Task<int> Update(bool check = false, CancellationToken cancellationToken = default)
        {
            if (!check && RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new NotSupportedException("Self-update requires a published Native AOT executable.");
            }
            string executablePath = Environment.ProcessPath ?? throw new NotSupportedException("The executable path is not available.");
            if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Run update from the published Mediana executable.");
            }

            using HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mediana-Updater");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            SelfUpdater updater = new(new GitHubReleaseClient(httpClient, ReleaseAssets.CurrentPlatform, new UpdateRetryPolicy(logger)),
                new UpdateInstaller(logger), logger);
            await updater.UpdateAsync(executablePath, check, timeout.Token);
            return CliExitCodes.Success;
        }
    }
}
