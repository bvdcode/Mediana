// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;

namespace Mediana.Cli.Updates
{
    internal static class ExecutableVersion
    {
        public static async Task<Version> ReadAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(path);
                if (metadata.ProductName != "Mediana.Cli")
                {
                    throw new InvalidDataException("The executable does not belong to Mediana.");
                }
                return Parse(metadata.ProductVersion);
            }

            ProcessStartInfo startInfo = new(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--version");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not read the executable version.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                string versionText = (await output).Trim();
                string heading = await error;
                if (process.ExitCode != 0 || !heading.StartsWith($"Mediana {versionText} |", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The executable did not report a valid Mediana version.");
                }
                return Parse(versionText);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }

        private static Version Parse(string? text)
        {
            if (text is null || !Version.TryParse(text.Split('+', '-')[0], out Version? version) || version.Build < 0)
            {
                throw new InvalidDataException("The executable does not contain a valid product version.");
            }
            return new Version(version.Major, version.Minor, version.Build);
        }
    }
}
