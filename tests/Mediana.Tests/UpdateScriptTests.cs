// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Mediana.Cli.Updates;

namespace Mediana.Tests
{
    public class UpdateScriptTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana update's (x64) ").FullName;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WindowsHelperWaitsForExitAndVerifiesStagedFile(bool corrupt)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = Path.Combine(directory, "custom mediana.exe");
            string staged = Path.Combine(directory, "staged.exe");
            string script = Path.Combine(directory, "apply update.ps1");
            string backup = Path.Combine(directory, "backup.exe");
            string log = Path.Combine(directory, "update.log");
            await File.WriteAllTextAsync(installed, "installed executable");
            byte[] payload = Encoding.UTF8.GetBytes("new executable");
            await File.WriteAllBytesAsync(staged, payload);
            await File.WriteAllTextAsync(script, UpdateScript.Content, new UTF8Encoding(false));
            byte[] hash = corrupt ? new byte[SHA256.HashSizeInBytes] : SHA256.HashData(payload);
            using Process parent = StartPowerShell(["-Command", "[Console]::In.ReadLine() | Out-Null"]);
            using Process helper = StartPowerShell(
            [
                "-ExecutionPolicy", "Bypass", "-File", script,
                "-ParentProcessId", parent.Id.ToString(),
                "-TargetPath", installed, "-StagedPath", staged, "-BackupPath", backup,
                "-LogPath", log, "-ExpectedSha256", Convert.ToHexString(hash), "-Version", "1.2.3"
            ]);
            try
            {
                Assert.Equal("ready", await helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.False(parent.HasExited);
                Assert.Equal("installed executable", await File.ReadAllTextAsync(installed));
                await parent.StandardInput.WriteLineAsync("exit");
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(corrupt ? 1 : 0, helper.ExitCode);
                Assert.Equal(corrupt ? "installed executable" : "new executable", await File.ReadAllTextAsync(installed));
                Assert.Contains(corrupt ? "SHA-256 verification failed" : "Updated successfully", await File.ReadAllTextAsync(log), StringComparison.Ordinal);
                Assert.False(File.Exists(staged));
                Assert.False(File.Exists(script));
                Assert.False(File.Exists(backup));
            }
            finally
            {
                foreach (Process process in new[] { helper, parent })
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
            }
        }

        [Fact]
        public async Task WindowsExecutableMetadataIdentifiesTheProduct()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            string executable = Path.Combine(AppContext.BaseDirectory, "Mediana.Cli.dll");
            FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(executable);
            Version version = await ExecutableVersion.ReadAsync(executable, CancellationToken.None);
            Assert.Equal(new Version(metadata.ProductMajorPart, metadata.ProductMinorPart, metadata.ProductBuildPart), version);
        }

        private static Process StartPowerShell(string[] arguments)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            ProcessStartInfo info = new(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            return Process.Start(info) ?? throw new InvalidOperationException("Could not start PowerShell.");
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
