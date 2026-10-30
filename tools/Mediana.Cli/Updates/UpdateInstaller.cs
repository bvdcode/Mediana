// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli.Updates
{
    internal class UpdateInstaller(ILogger logger)
    {
        public async Task InstallAsync(string executablePath, string stagedPath, GitHubRelease release, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.Move(stagedPath, executablePath, overwrite: true);
                return;
            }
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Executable replacement supports Windows, Linux and macOS.");
            }

            string scriptPath = Path.ChangeExtension(stagedPath, ".ps1");
            bool started = false;
            try
            {
                await File.WriteAllTextAsync(scriptPath, UpdateScript.Content, new UTF8Encoding(false), token);
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                ProcessStartInfo startInfo = new(powershell)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                string[] arguments =
                [
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                    "-ParentProcessId", Environment.ProcessId.ToString(),
                    "-TargetPath", executablePath, "-StagedPath", stagedPath,
                    "-BackupPath", Path.ChangeExtension(stagedPath, ".bak"),
                    "-LogPath", executablePath + ".update.log",
                    "-ExpectedSha256", Convert.ToHexString(release.Sha256), "-Version", release.Version.ToString()
                ];
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the update helper.");
                try
                {
                    string? ready = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
                    if (ready != "ready")
                    {
                        throw new InvalidOperationException("The update helper could not prepare the installation.");
                    }
                    started = true;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to start the update helper.");
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                    throw;
                }
            }
            finally
            {
                if (!started)
                {
                    File.Delete(scriptPath);
                }
            }
        }
    }
}
