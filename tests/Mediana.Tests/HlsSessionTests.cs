// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsSessionTests
    {
        [Fact]
        public async Task RealVideoAudioIndexPlaylistAndOutOfOrderSegmentsPreserveAllPackets() =>
            await HlsSessionTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Fact]
        public async Task SegmentRequestReadsOnlyItsIndexedPayloadRanges()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-segments.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 31);
            List<Mp4SampleInfo> expected = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                for (int sample = 30; sample < 36; sample++)
                {
                    expected.Add(reader.GetSampleInfo(0, sample));
                }
                for (int sample = 264; sample < 339; sample++)
                {
                    expected.Add(reader.GetSampleInfo(1, sample));
                }
            }
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            source.Reads.Clear();
            long retained = allocator.AllocatedBytes;
            using MemoryStream output = new();
            await session.WriteSegmentAsync(2, output);
            Assert.Equal(expected.Sum(info => info.Size), source.Reads.Sum(read => read.Length));
            Assert.All(source.Reads, read => Assert.Contains(expected,
                info => read.Offset >= info.Offset && read.Offset + read.Length <= info.Offset + info.Size));
            Assert.Equal(retained, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CancelledOrFailedSourceReadReleasesWorkersAndAllowsSameSegmentRetry(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"), 17);
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            using MemoryStream failed = new();
            long retained = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            if (cancel)
            {
                source.AfterRead = cancellation.Cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteSegmentAsync(2, failed, cancellation.Token).AsTask());
            }
            else
            {
                source.AfterRead = () => throw new IOException("The sample source failed.");
                await Assert.ThrowsAsync<IOException>(() => session.WriteSegmentAsync(2, failed).AsTask());
            }
            source.AfterRead = null;
            Assert.Equal(retained, allocator.AllocatedBytes);
            using MemoryStream retry = new();
            await session.WriteSegmentAsync(2, retry);
            using MemoryStream repeated = new();
            await session.WriteSegmentAsync(2, repeated);
            Assert.Equal(retry.ToArray(), repeated.ToArray());
            Assert.Equal(retained, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PartialOutputOrFlushFailureCanRetryFromSameImmutableIndex(bool flush)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            using MpegTsTestOutput failed = new() { FailOnFlush = flush, FailOnWrite = flush ? null : 2 };
            long retained = allocator.AllocatedBytes;
            await Assert.ThrowsAsync<IOException>(() => session.WriteSegmentAsync(1, failed).AsTask());
            Assert.False(failed.WasDisposed);
            Assert.Equal(retained, allocator.AllocatedBytes);
            using MemoryStream retry = new();
            await session.WriteSegmentAsync(1, retry);
            Assert.NotEmpty(retry.ToArray());
            Assert.Equal(retained, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ConcurrentReadDisposeAndWriteAreRejectedWithoutInterruptingActiveOutput()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            using MemoryStream rejected = new();
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using MpegTsTestOutput output = new() { Gate = gate };
            Task active = session.WriteSegmentAsync(2, output).AsTask();
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteSegmentAsync(0, rejected).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            gate.SetResult();
            await active;
            Assert.NotEmpty(output.Bytes.ToArray());
            Assert.Empty(rejected.ToArray());
        }

        [Fact]
        public async Task FailedOpenReleasesReaderAndBorrowsSource()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            Mp4HlsOptions options = HlsSessionTestInput.Options() with { Transport = new() { MaximumPacketBytes = 1 } };
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0, 1, options).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task VideoOnlySelectionDoesNotReadAudioAndPublishesOnlyOneTrack()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-segments.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            List<Mp4SampleInfo> audio = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                for (int sample = 0; sample < 339; sample++)
                {
                    audio.Add(reader.GetSampleInfo(1, sample));
                }
            }
            source.Reads.Clear();
            await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, options: HlsSessionTestInput.Options());
            Assert.Single(session.Streams);
            Assert.All(session.Segments, segment => Assert.Null(segment.Audio));
            using MemoryStream output = new();
            await session.WriteSegmentAsync(1, output);
            Assert.DoesNotContain(source.Reads, read => audio.Any(info => read.Offset < info.Offset + info.Size && read.Offset + read.Length > info.Offset));
        }
    }
}
