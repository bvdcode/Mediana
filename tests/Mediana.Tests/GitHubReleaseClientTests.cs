// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Text.Json.Nodes;
using Mediana.Cli.Updates;

namespace Mediana.Tests
{
    public class GitHubReleaseClientTests : IDisposable
    {
        private static readonly byte[] Payload = [1, 2, 3, 4];
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-release-").FullName;

        [Theory]
        [InlineData((int)ReleasePlatform.WindowsX64, "Mediana-1.2.3-win-x64.exe")]
        [InlineData((int)ReleasePlatform.LinuxX64, "Mediana-1.2.3-linux-x64")]
        [InlineData((int)ReleasePlatform.MacOsArm64, "Mediana-1.2.3-osx-arm64")]
        public async Task ExactPlatformExecutableIsDownloadedAndVerified(int platform, string expected)
        {
            using UpdateHttpHandler handler = new(UpdateTestRelease.Metadata("1.2.3", Payload).ToJsonString(), Payload);
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, (ReleasePlatform)platform);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            string destination = Path.Combine(directory, "download");
            await client.DownloadAsync(release, destination, CancellationToken.None);
            Assert.Equal(new Version(1, 2, 3), release.Version);
            Assert.Equal($"https://github.com/bvdcode/Mediana/releases/download/v1.2.3/{expected}", release.DownloadUrl.AbsoluteUri);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(2, handler.RequestCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("sha512:abcd")]
        [InlineData("sha256:1234")]
        [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
        public async Task MissingOrMalformedChecksumIsRejected(string? digest)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["assets"]![0]!["digest"] = digest;
            await AssertMetadataRejectedAsync(metadata);
        }

        [Theory]
        [InlineData("v1.2.3-preview")]
        [InlineData("v1.2.3.4")]
        [InlineData("1.2.3")]
        [InlineData("v1.2")]
        public async Task UnsupportedReleaseTagIsRejected(string tag)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["tag_name"] = tag;
            await AssertMetadataRejectedAsync(metadata);
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("prerelease")]
        public async Task UnpublishedOrPrereleaseBuildIsRejected(string property)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata[property] = true;
            await AssertMetadataRejectedAsync(metadata);
        }

        [Theory]
        [InlineData("https://example.com/Mediana-1.2.3-win-x64.exe")]
        [InlineData("http://github.com/bvdcode/Mediana/releases/download/v1.2.3/Mediana-1.2.3-win-x64.exe")]
        [InlineData("https://github.com/another/Mediana/releases/download/v1.2.3/Mediana-1.2.3-win-x64.exe")]
        public async Task ForeignDownloadUrlIsRejected(string url)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["assets"]![0]!["browser_download_url"] = url;
            await AssertMetadataRejectedAsync(metadata);
        }

        [Fact]
        public async Task MissingPlatformAssetDoesNotSelectAnotherPlatform()
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["assets"]!.AsArray().RemoveAt(0);
            await AssertMetadataRejectedAsync(metadata);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task InvalidSizeIsRejected(int size)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["assets"]![0]!["size"] = size;
            await AssertMetadataRejectedAsync(metadata);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        public async Task IncorrectDownloadLengthIsRejected(int size)
        {
            JsonObject metadata = UpdateTestRelease.Metadata("1.2.3", Payload);
            metadata["assets"]![0]!["size"] = size;
            using HttpClient http = new(new UpdateHttpHandler(metadata.ToJsonString(), Payload));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, Path.Combine(directory, "download"), CancellationToken.None));
        }

        [Fact]
        public async Task HttpFailureIsReported()
        {
            using HttpClient http = new(new UpdateHttpHandler("{}", [], status: HttpStatusCode.Forbidden));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64);
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ExistingDownloadDestinationIsPreserved()
        {
            string destination = Path.Combine(directory, "existing");
            await File.WriteAllBytesAsync(destination, Payload);
            using UpdateHttpHandler handler = new(UpdateTestRelease.Metadata("1.2.3", Payload).ToJsonString(), Payload);
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(release, destination, CancellationToken.None));
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(1, handler.RequestCount);
        }

        private static async Task AssertMetadataRejectedAsync(JsonObject metadata)
        {
            using HttpClient http = new(new UpdateHttpHandler(metadata.ToJsonString(), Payload));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestAsync(CancellationToken.None));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
