// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoPresentationReaderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OverlapsAreClippedGapsPreservedAndSelectedTailEndsExactly(bool selected)
        {
            MediaBufferAllocator allocator = new(8);
            long[] times = [-50, 0, 42, 83, 125];
            long[] lengths = [70, 20, 42, 70, 20];
            int reads = 0;
            ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                int index = reads++;
                return ValueTask.FromResult(index == times.Length ? null : Create(allocator, times[index], lengths[index]));
            }
            MediaTimeRange? range = selected ? new(new(1, new(1, 1000)), new(110, new(1, 1000))) : null;
            await using VideoPresentationReader reader = new(ReadAsync, range);
            long[] expectedTimes = selected ? [42, 83] : times;
            long[] expectedDurations = selected ? [41, 27] : [50, 20, 41, 42, 20];
            for (int index = 0; index < expectedTimes.Length; index++)
            {
                using VideoFrame frame = (await reader.ReadFrameAsync())!;
                Assert.Equal(new MediaTimestamp(expectedTimes[index], new(1, 1000)), frame.Timestamp);
                Assert.Equal(new MediaTimestamp(expectedDurations[index], new(1, 1000)), new(frame.Duration!.Value.Value, frame.Duration.Value.TimeBase));
                Assert.Equal(unchecked((byte)expectedTimes[index]), frame.Data.Span[0]);
            }
            Assert.Null(await reader.ReadFrameAsync());
            Assert.Null(await reader.ReadFrameAsync());
            Assert.Equal(selected ? 5 : 6, reads);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task InvalidTimingAndCancellationReleaseBothLookaheadAndCurrent(int failure)
        {
            MediaBufferAllocator allocator = new(8);
            using CancellationTokenSource cancellation = new();
            int reads = 0;
            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                reads++;
                if (failure == 0) { return Create(allocator, 0, 10); }
                if (failure == 1) { return Create(allocator, 0, null); }
                VideoFrame frame = Create(allocator, reads * 10, 10);
                if (reads == 2) { await cancellation.CancelAsync(); }
                return frame;
            }
            await using VideoPresentationReader reader = new(ReadAsync);
            if (failure == 0) { await Assert.ThrowsAsync<InvalidDataException>(async () => { using VideoFrame? frame = await reader.ReadFrameAsync(); }); }
            else if (failure == 1) { await Assert.ThrowsAsync<NotSupportedException>(async () => { using VideoFrame? frame = await reader.ReadFrameAsync(); }); }
            else { await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using VideoFrame? frame = await reader.ReadFrameAsync(cancellation.Token); }); }
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using VideoFrame? frame = await reader.ReadFrameAsync(); });
        }

        private static VideoFrame Create(MediaBufferAllocator allocator, long time, long? duration)
        {
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            writable.Memory.Span.Fill(unchecked((byte)time));
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, format.CreateTightLayout(), new(time, new(1, 1000)),
                duration is long length ? new MediaDuration(length, new(1, 1000)) : null);
        }
    }
}
