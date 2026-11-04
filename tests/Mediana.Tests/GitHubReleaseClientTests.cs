// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Text;
using Mediana.Cli.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mediana.Tests
{
    public class GitHubReleaseClientTests : IDisposable
    {
        private static readonly byte[] Payload = [1, 2, 3, 4];
        private static readonly UpdateRetryPolicy Retry = new(NullLogger.Instance);
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-release-").FullName;

        [Theory]
        [InlineData((int)ReleasePlatform.WindowsX64, "Mediana-1.2.3-win-x64.exe")]
        [InlineData((int)ReleasePlatform.LinuxX64, "Mediana-1.2.3-linux-x64")]
        [InlineData((int)ReleasePlatform.MacOsArm64, "Mediana-1.2.3-osx-arm64")]
        public async Task ExactPlatformExecutableIsDownloadedAndVerifiedWithoutApiRequests(int platform, string expected)
        {
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, (ReleasePlatform)platform, Retry);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            string destination = Path.Combine(directory, "download");
            await client.DownloadAsync(release, destination, CancellationToken.None);
            Assert.Equal(new Version(1, 2, 3), release.Version);
            Assert.Equal($"https://github.com/bvdcode/Mediana/releases/download/v1.2.3/{expected}", release.DownloadUrl.AbsoluteUri);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(4, handler.RequestCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("sha256:1234")]
        [InlineData("not a checksum")]
        [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  Mediana-1.2.3-win-x64.exe")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000  Mediana-1.2.3-linux-x64")]
        [InlineData("0000000000000000000000000000000000000000000000000000000000000000 *Mediana-1.2.3-win-x64.exe")]
        public async Task MalformedOrWrongPlatformChecksumIsRejected(string checksum)
        {
            await AssertMetadataRejectedAsync(new UpdateTestRelease(Payload) { ChecksumOverride = checksum });
        }

        [Fact]
        public async Task OversizedChecksumIsRejectedWithoutReadingTheWholeBody()
        {
            byte[] payload = new byte[4096];
            using FragmentedReadStream body = new(payload, maximumRead: 1);
            using UpdateRetryHttpHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }
                : UpdateRetryHttpHandler.Release(request, Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestAsync(CancellationToken.None));
            Assert.Equal(2, handler.RequestCount);
            Assert.InRange(body.AsyncReads, 1, 128);
            Assert.Equal(0, body.SyncReads);
            Assert.False(body.CanRead);
        }

        [Fact]
        public async Task FragmentedChecksumIsReadToCompletionAndDisposed()
        {
            using FragmentedReadStream body = new(Encoding.ASCII.GetBytes(new string('0', 64) + "  Mediana-1.2.3-win-x64.exe\r\n"), maximumRead: 1);
            using UpdateRetryHttpHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }
                : UpdateRetryHttpHandler.Release(request, Payload));
            using HttpClient http = new(handler);
            GitHubRelease release = await new GitHubReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None);
            Assert.Equal(new byte[32], release.Sha256);
            Assert.Equal(3, handler.RequestCount);
            Assert.Equal(0, body.SyncReads);
            Assert.False(body.CanRead);
        }

        [Theory]
        [InlineData("")]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public async Task PublishedChecksumLineEndingsAreAccepted(string ending)
        {
            string name = ReleaseAssets.GetName(ReleasePlatform.WindowsX64, new Version(1, 2, 3));
            UpdateTestRelease metadata = new(Payload) { ChecksumOverride = new string('0', 64) + "  " + name + ending };
            using HttpClient http = new(new UpdateHttpHandler(metadata));
            GitHubRelease release = await new GitHubReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None);
            Assert.Equal(new byte[32], release.Sha256);
        }

        [Theory]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v1.2.3-preview")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v1.2.3.4")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/1.2.3")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v1.2")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v01.2.3")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v1.2.3?other=4")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/tag/v1.2.3#other")]
        [InlineData("https://github.com/bvdcode/Mediana/releases/latest")]
        [InlineData("https://example.com/bvdcode/Mediana/releases/tag/v1.2.3")]
        [InlineData("http://github.com/bvdcode/Mediana/releases/tag/v1.2.3")]
        [InlineData("https://github.com/another/Mediana/releases/tag/v1.2.3")]
        public async Task UnrecognizedReleaseRedirectIsRejectedBeforeRequestingAssets(string url)
        {
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload) { ReleaseUri = new Uri(url) });
            using HttpClient http = new(handler);
            await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
            Assert.Equal(1, handler.RequestCount);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(null)]
        public async Task InvalidOrMissingSizeIsRejected(long? size)
        {
            await AssertMetadataRejectedAsync(new UpdateTestRelease(Payload) { Size = size });
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        public async Task IncorrectDownloadLengthIsRejected(int size)
        {
            using HttpClient http = new(new UpdateHttpHandler(new UpdateTestRelease(Payload) { Size = size }));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, Path.Combine(directory, "download"), CancellationToken.None));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task MissingReleaseOrPlatformAssetIsReportedWithoutSelectingAnotherAsset(int failedRequest)
        {
            using UpdateRetryHttpHandler handler = new((request, attempt) => attempt == failedRequest
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : UpdateRetryHttpHandler.Release(request, Payload));
            using HttpClient http = new(handler);
            HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubReleaseClient(http, ReleasePlatform.WindowsX64, Retry).GetLatestAsync(CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
            Assert.Equal(failedRequest, handler.RequestCount);
        }

        [Fact]
        public async Task HttpFailureIsReported()
        {
            using HttpClient http = new(new UpdateHttpHandler(new UpdateTestRelease([]), status: HttpStatusCode.Forbidden));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ExistingDownloadDestinationIsPreserved()
        {
            string destination = Path.Combine(directory, "existing");
            await File.WriteAllBytesAsync(destination, Payload);
            using UpdateHttpHandler handler = new(new UpdateTestRelease(Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(release, destination, CancellationToken.None));
            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(3, handler.RequestCount);
        }

        private static async Task AssertMetadataRejectedAsync(UpdateTestRelease metadata)
        {
            using HttpClient http = new(new UpdateHttpHandler(metadata));
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, Retry);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestAsync(CancellationToken.None));
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
