// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Net;
using System.Net.Sockets;
using System.Text;
using Mediana.Cli.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mediana.Tests
{
    public class UpdateRetryTests : IDisposable
    {
        private static readonly byte[] Payload = [1, 2, 3, 4];
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-retry-").FullName;
        private readonly List<TimeSpan> delays = [];

        [Fact]
        public async Task SocketAccessDenialRecoversWithExponentialDelays()
        {
            ProgressTestContext log = new();
            UpdateRetryPolicy retry = new(log, RecordDelayAsync);
            using UpdateRetryHttpHandler handler = new((_, attempt) => attempt < 3
                ? throw new HttpRequestException("Socket access denied.", new SocketException((int)SocketError.AccessDenied))
                : UpdateRetryHttpHandler.Metadata(Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, retry);

            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);

            Assert.Equal(new Version(1, 2, 3), release.Version);
            Assert.Equal(3, handler.RequestCount);
            Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
            Assert.Equal(2, log.Messages.Count);
            Assert.Contains("attempt 2/6", log.Messages[0], StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(408, true)]
        [InlineData(429, true)]
        [InlineData(500, true)]
        [InlineData(502, true)]
        [InlineData(503, true)]
        [InlineData(504, true)]
        [InlineData(400, false)]
        [InlineData(401, false)]
        [InlineData(403, false)]
        [InlineData(404, false)]
        public async Task OnlyTransientHttpFailuresAreRetried(int status, bool transient)
        {
            using UpdateRetryHttpHandler handler = new((_, attempt) => attempt == 1
                ? new HttpResponseMessage((HttpStatusCode)status)
                : UpdateRetryHttpHandler.Metadata(Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));

            if (transient)
            {
                await client.GetLatestAsync(CancellationToken.None);
                Assert.Equal(2, handler.RequestCount);
                Assert.Single(delays);
            }
            else
            {
                HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestAsync(CancellationToken.None));
                Assert.Equal((HttpStatusCode)status, error.StatusCode);
                Assert.Equal(1, handler.RequestCount);
                Assert.Empty(delays);
            }
        }

        [Fact]
        public async Task PersistentNetworkFailureStopsAfterSixAttempts()
        {
            using UpdateRetryHttpHandler handler = new((_, _) => throw new HttpRequestException("Connection refused."));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));

            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestAsync(CancellationToken.None));

            Assert.Equal(6, handler.RequestCount);
            Assert.Equal(new[] { 1, 2, 4, 8, 16 }.Select(seconds => TimeSpan.FromSeconds(seconds)), delays);
        }

        [Fact]
        public async Task CancellationDuringDelayStopsBeforeAnotherRequest()
        {
            using CancellationTokenSource source = new();
            UpdateRetryPolicy retry = new(NullLogger.Instance, async (_, token) =>
            {
                await source.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
            using UpdateRetryHttpHandler handler = new((_, _) => throw new HttpRequestException("Connection refused."));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, retry);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLatestAsync(source.Token));

            Assert.Equal(1, handler.RequestCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RequestTimeoutRetriesButCallerCancellationStops(bool cancel)
        {
            using CancellationTokenSource source = new();
            using UpdateRetryHttpHandler handler = new((_, attempt) =>
            {
                if (attempt > 1)
                {
                    return UpdateRetryHttpHandler.Metadata(Payload);
                }
                if (cancel)
                {
                    source.Cancel();
                }
                throw new TaskCanceledException("Request timed out.");
            });
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));

            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLatestAsync(source.Token));
                Assert.Equal(1, handler.RequestCount);
                Assert.Empty(delays);
            }
            else
            {
                await client.GetLatestAsync(source.Token);
                Assert.Equal(2, handler.RequestCount);
                Assert.Single(delays);
            }
        }

        [Fact]
        public async Task InterruptedMetadataBodyIsDisposedAndRequestedAgain()
        {
            using InterruptedUpdateStream interrupted = new(Encoding.UTF8.GetBytes(UpdateTestRelease.Metadata("1.2.3", Payload).ToJsonString()));
            using UpdateRetryHttpHandler handler = new((_, attempt) => attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(interrupted) }
                : UpdateRetryHttpHandler.Metadata(Payload));
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));

            await client.GetLatestAsync(CancellationToken.None);

            Assert.True(interrupted.IsDisposed);
            Assert.Equal(2, handler.RequestCount);
            Assert.Single(delays);
        }

        [Fact]
        public async Task InterruptedDownloadIsTruncatedBeforeTheNextAttempt()
        {
            using InterruptedUpdateStream interrupted = new([9, 9, 9, 9, 9, 9, 9, 9]);
            using UpdateRetryHttpHandler handler = new((request, attempt) => request.RequestUri!.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal)
                ? UpdateRetryHttpHandler.Metadata(Payload)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = attempt == 2 ? new StreamContent(interrupted) : new ByteArrayContent(Payload)
                });
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);
            string destination = Path.Combine(directory, "download");

            await client.DownloadAsync(release, destination, CancellationToken.None);

            Assert.Equal(Payload, await File.ReadAllBytesAsync(destination));
            Assert.True(interrupted.IsDisposed);
            Assert.Equal(3, handler.RequestCount);
            Assert.Single(delays);
        }

        [Fact]
        public async Task ChecksumFailureDoesNotRetryTheDownload()
        {
            using UpdateRetryHttpHandler handler = new((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal)
                ? UpdateRetryHttpHandler.Metadata(Payload)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 3, 2, 1]) });
            using HttpClient http = new(handler);
            GitHubReleaseClient client = new(http, ReleasePlatform.WindowsX64, new UpdateRetryPolicy(NullLogger.Instance, RecordDelayAsync));
            GitHubRelease release = await client.GetLatestAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, Path.Combine(directory, "download"), CancellationToken.None));

            Assert.Equal(2, handler.RequestCount);
            Assert.Empty(delays);
        }

        private Task RecordDelayAsync(TimeSpan interval, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            delays.Add(interval);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
