// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Mediana.Cli.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mediana.Tests
{
    public class SelfUpdaterTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana update's ").FullName;

        [Theory]
        [InlineData("1.2.2", false)]
        [InlineData("1.2.1", false)]
        [InlineData("1.2.3", true)]
        public async Task VersionCheckDoesNotDownloadOrChangeInstalledFile(string latestVersion, bool checkOnly)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = await CreateInstalledAsync();
            byte[] original = await File.ReadAllBytesAsync(installed);
            using UpdateHttpHandler handler = Handler(latestVersion, Executable(latestVersion));
            await UpdateAsync(handler, installed, checkOnly);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(original, await File.ReadAllBytesAsync(installed));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task RenamedRunningExecutableIsReplacedAndPermissionsArePreserved()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = await CreateInstalledAsync();
            UnixFileMode permissions = File.GetUnixFileMode(installed);
            using Process process = Process.Start(new ProcessStartInfo(installed)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Could not start the installed executable.");
            try
            {
                Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                byte[] replacement = Executable("1.2.3");
                using UpdateHttpHandler handler = Handler("1.2.3", replacement);
                await UpdateAsync(handler, installed);
                Assert.False(process.HasExited);
                Assert.Equal(replacement, await File.ReadAllBytesAsync(installed));
                Assert.Equal(permissions, File.GetUnixFileMode(installed));
                Assert.Equal(new Version(1, 2, 3), await ExecutableVersion.ReadAsync(installed, CancellationToken.None));
                Assert.Single(Directory.GetFiles(directory));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                }
            }
        }

        [Theory]
        [InlineData("1.2.2", "Mediana")]
        [InlineData("1.2.3", "AnotherProduct")]
        public async Task WrongProductOrVersionPreservesInstalledFile(string version, string product)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = await CreateInstalledAsync();
            byte[] original = await File.ReadAllBytesAsync(installed);
            using UpdateHttpHandler handler = Handler("1.2.3", Executable(version, product));
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdateAsync(handler, installed));
            Assert.Equal(original, await File.ReadAllBytesAsync(installed));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task CorruptDownloadPreservesInstalledFileAndCleansStaging()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = await CreateInstalledAsync();
            byte[] original = await File.ReadAllBytesAsync(installed);
            byte[] payload = Executable("1.2.3");
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", payload);
            int assetIndex = OperatingSystem.IsMacOS() ? 2 : 1;
            metadata["assets"]![assetIndex]!["digest"] = "sha256:" + new string('0', 64);
            using UpdateHttpHandler handler = new(metadata.ToJsonString(), payload);
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdateAsync(handler, installed));
            Assert.Equal(original, await File.ReadAllBytesAsync(installed));
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task CancelledDownloadPreservesInstalledFileAndCleansStaging()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string installed = await CreateInstalledAsync();
            byte[] original = await File.ReadAllBytesAsync(installed);
            byte[] payload = Executable("1.2.3");
            using CancellationTokenSource source = new();
            using UpdateHttpHandler handler = new(UpdateTestRelease.Metadata("1.2.3", payload).ToJsonString(), payload, source.Cancel);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateAsync(handler, installed, token: source.Token));
            Assert.Equal(original, await File.ReadAllBytesAsync(installed));
            Assert.Single(Directory.GetFiles(directory));
        }

        private async Task<string> CreateInstalledAsync()
        {
            string path = Path.Combine(directory, "custom mediana");
            await File.WriteAllBytesAsync(path, Executable("1.2.2"));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            }
            return path;
        }

        private static byte[] Executable(string version, string product = "Mediana")
        {
            return Encoding.UTF8.GetBytes($"#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then\n printf '%s\\n' '{version}'\n printf '%s\\n' '{product} {version} | Copyright' >&2\nelse\n printf 'ready\\n'\n read -r input\nfi\n");
        }

        private static UpdateHttpHandler Handler(string version, byte[] payload)
        {
            return new UpdateHttpHandler(UpdateTestRelease.Metadata(version, payload).ToJsonString(), payload);
        }

        private static async Task UpdateAsync(UpdateHttpHandler handler, string installed, bool checkOnly = false, CancellationToken token = default)
        {
            using HttpClient http = new(handler);
            SelfUpdater updater = new(new GitHubReleaseClient(http, ReleaseAssets.CurrentPlatform, new UpdateRetryPolicy(NullLogger.Instance)),
                new UpdateInstaller(NullLogger.Instance), NullLogger.Instance);
            await updater.UpdateAsync(installed, checkOnly, token);
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
