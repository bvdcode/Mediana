// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mediana.Cli.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mediana.Tests
{
    public class ReleaseClientTests : IDisposable
    {
        private static readonly byte[] Payload = [1, 2, 3, 4];
        private static readonly UpdateRetryPolicy Retry = new(NullLogger.Instance);
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-release-").FullName;

        [Theory]
        [InlineData((int)ReleasePlatform.WindowsX64, "Mediana-1.2.3-win-x64.exe")]
        [InlineData((int)ReleasePlatform.LinuxX64, "Mediana-1.2.3-linux-x64")]
        [InlineData((int)ReleasePlatform.MacOsArm64, "Mediana-1.2.3-osx-arm64")]
        public async Task ExactPlatformExecutableIsDownloadedFromTheReleaseCdn(int platform, string expected)
        {
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload));
            using HttpClient http = new(handler);
            ReleaseClient client = new(http, (ReleasePlatform)platform, Retry);
            UpdateRelease release = await client.GetLatestAsync(CancellationToken.None);
            string destination = Path.Combine(directory, "download");

            await client.DownloadAsync(release, destination, CancellationToken.None);

            Assert.Equal(new Version(1, 2, 3), release.Version);
            Assert.Equal($"https://releases.splidex.com/mediana/1.2.3/{expected}", release.DownloadUrl.AbsoluteUri);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(2, handler.RequestCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("sha256:1234")]
        [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
        public async Task MissingOrMalformedChecksumIsRejected(string? checksum)
        {
            UpdateTestRelease metadata = new(Payload);
            metadata.Manifest["assets"]![0]!["sha256"] = checksum;
            await AssertManifestRejectedAsync(metadata);
        }

        [Theory]
        [InlineData("v1.2.3")]
        [InlineData("1.2.3-preview")]
        [InlineData("1.2.3.4")]
        [InlineData("1.2")]
        [InlineData("01.2.3")]
        [InlineData("../1.2.3")]
        [InlineData("1.2.3?other=4")]
        public async Task UnstableOrNoncanonicalVersionIsRejected(string version)
        {
            UpdateTestRelease metadata = new(Payload);
            metadata.Manifest["version"] = version;
            await AssertManifestRejectedAsync(metadata);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(null)]
        public async Task InvalidOrMissingSizeIsRejected(long? size)
        {
            UpdateTestRelease metadata = new(Payload);
            metadata.Manifest["assets"]![0]!["size"] = size;
            await AssertManifestRejectedAsync(metadata);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MissingOrDuplicatePlatformAssetIsRejected(bool duplicate)
        {
            UpdateTestRelease metadata = new(Payload);
            JsonArray assets = metadata.Manifest["assets"]!.AsArray();
            if (duplicate)
            {
                assets.Add(assets[0]!.DeepClone());
            }
            else
            {
                assets.RemoveAt(0);
            }
            await AssertManifestRejectedAsync(metadata);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("{\"version\":\"1.2.3\"}")]
        [InlineData("{\"version\":\"1.2.3\",\"assets\":{}}")]
        public async Task IncompleteManifestIsRejected(string manifest)
        {
            using UpdateRetryHttpHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) });
            using HttpClient http = new(handler);
            await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task OversizedManifestIsRejectedWithoutReadingTheWholeBody()
        {
            using FragmentedReadStream body = new(new byte[32 * 1024], maximumRead: 1);
            using UpdateRetryHttpHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
            using HttpClient http = new(handler);

            await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));

            Assert.InRange(body.AsyncReads, 1, 16 * 1024 + 1);
            Assert.Equal(0, body.SyncReads);
            Assert.False(body.CanRead);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task FragmentedManifestIsReadToCompletionAndDisposed()
        {
            using FragmentedReadStream body = new(Encoding.UTF8.GetBytes(new UpdateTestRelease(Payload).Manifest.ToJsonString()), maximumRead: 1);
            using UpdateRetryHttpHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
            using HttpClient http = new(handler);

            UpdateRelease release = await new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None);

            Assert.Equal(SHA256.HashData(Payload), release.Sha256);
            Assert.Equal(0, body.SyncReads);
            Assert.False(body.CanRead);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task ManifestCannotRedirectExecutableDownloadsToAnotherOrigin()
        {
            UpdateTestRelease metadata = new(Payload);
            metadata.Manifest["assets"]![0]!["url"] = "https://example.com/untrusted.exe";
            using HttpClient http = new(new UpdateHttpHandler(metadata));

            UpdateRelease release = await new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None);

            Assert.Equal("https://releases.splidex.com/mediana/1.2.3/Mediana-1.2.3-win-x64.exe", release.DownloadUrl.AbsoluteUri);
        }

        [Fact]
        public async Task MalformedJsonIsRejectedWithoutDownloadingAnExecutable()
        {
            using UpdateRetryHttpHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{invalid") });
            using HttpClient http = new(handler);
            await Assert.ThrowsAnyAsync<JsonException>(() => new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
            Assert.Equal(1, handler.RequestCount);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        public async Task IncorrectDownloadLengthIsRejected(int size)
        {
            UpdateTestRelease metadata = new(Payload);
            metadata.Manifest["assets"]![0]!["size"] = size;
            using HttpClient http = new(new UpdateHttpHandler(metadata));
            ReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            UpdateRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, Path.Combine(directory, "download"), CancellationToken.None));
        }

        [Fact]
        public async Task MissingManifestIsReportedWithoutUsingAnotherSource()
        {
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload), status: HttpStatusCode.NotFound);
            using HttpClient http = new(handler);
            HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
            Assert.Equal(1, handler.RequestCount);
        }

        [Fact]
        public async Task ExistingDownloadDestinationIsPreserved()
        {
            string destination = Path.Combine(directory, "existing");
            await File.WriteAllBytesAsync(destination, Payload);
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload));
            using HttpClient http = new(handler);
            ReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            UpdateRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(release, destination, CancellationToken.None));
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(1, handler.RequestCount);
        }

        private static async Task AssertManifestRejectedAsync(UpdateTestRelease metadata)
        {
            using HttpClient http = new(new UpdateHttpHandler(metadata));
            await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
