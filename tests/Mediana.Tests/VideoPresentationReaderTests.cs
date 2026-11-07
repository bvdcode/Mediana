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

        [Theory]
        [InlineData(0, 120, true)]
        [InlineData(1, 120, false)]
        [InlineData(0, 119, false)]
        [InlineData(0, 121, false)]
        public async Task CompleteFrameIntervalsRejectPartialBoundariesAndEarlyEnd(int start, int end, bool valid)
        {
            MediaBufferAllocator allocator = new(8);
            int position = 0;
            ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(position == 3 ? null : Create(allocator, position++ * 40, 40));
            }
            await using VideoPresentationReader reader = new(ReadAsync, new(new(start, new(1, 1000)), new(end, new(1, 1000))), VideoIntervalAlignment.CompleteFrames);
            async Task DrainAsync()
            {
                while (true) { using VideoFrame? frame = await reader.ReadFrameAsync(); if (frame is null) { break; } }
            }
            if (valid) { await DrainAsync(); }
            else if (start != 0 || end > 120) { await Assert.ThrowsAsync<InvalidDataException>(DrainAsync); }
            else { await Assert.ThrowsAsync<NotSupportedException>(DrainAsync); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(40)]
        [InlineData(80)]
        public async Task CompleteFrameIntervalsRejectGapsEvenWhenNextFrameStartsAtSelectedEnd(int end)
        {
            MediaBufferAllocator allocator = new(8);
            int position = 0;
            ValueTask<VideoFrame?> ReadAsync(CancellationToken token) =>
                ValueTask.FromResult<VideoFrame?>(position == 3 ? null : Create(allocator, position++ * 40, 20));
            await using VideoPresentationReader reader = new(ReadAsync,
                new(new(0, new(1, 1000)), new(end, new(1, 1000))), VideoIntervalAlignment.CompleteFrames);
            using (VideoFrame frame = (await reader.ReadFrameAsync())!) { Assert.NotNull(frame); }
            await Assert.ThrowsAsync<InvalidDataException>(async () => { using VideoFrame? frame = await reader.ReadFrameAsync(); });
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1, 101, true)]
        [InlineData(2, 101, false)]
        [InlineData(1, 100, false)]
        public async Task SourceBoundsTrimOpeningAndClosingPicturesBeforeStrictRequestedAlignment(int start, int end, bool valid)
        {
            MediaBufferAllocator allocator = new(8);
            long[] times = [-100, -30, 10, 25, 40];
            long[] lengths = [200, 60, 15, 15, 30];
            int reads = 0;
            ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                int index = reads++;
                return ValueTask.FromResult(index == times.Length ? null : Create(allocator, times[index], lengths[index]));
            }
            TimeBase clock = new(1, 3000);
            await using VideoPresentationReader reader = new(ReadAsync, new(new(start, clock), new(end, clock)),
                VideoIntervalAlignment.CompleteFrames, new(new(1, clock), new(101, clock)));
            async Task DrainAsync()
            {
                long[] expectedTimes = [1, 30, 75];
                long[] expectedDurations = [29, 45, 26];
                int count = 0;
                while (true)
                {
                    using VideoFrame? frame = await reader.ReadFrameAsync();
                    if (frame is null) { break; }
                    if (valid)
                    {
                        Assert.Equal(new MediaTimestamp(expectedTimes[count], clock), frame.Timestamp);
                        Assert.Equal(new MediaTimestamp(expectedDurations[count], clock), new(frame.Duration!.Value.Value, frame.Duration.Value.TimeBase));
                        Assert.Equal(unchecked((byte)times[count + 1]), frame.Data.Span[0]);
                    }
                    count++;
                }
                if (valid) { Assert.Equal(3, count); }
            }
            if (valid) { await DrainAsync(); }
            else if (start != 1) { await Assert.ThrowsAsync<InvalidDataException>(DrainAsync); }
            else { await Assert.ThrowsAsync<NotSupportedException>(DrainAsync); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(-20, -10)]
        [InlineData(20, 30)]
        [InlineData(5, 5)]
        public async Task DisjointOrEmptySourceAndRequestedRangesDoNotReadFrames(int start, int end)
        {
            int reads = 0;
            ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                reads++;
                throw new InvalidOperationException("Empty selections must not read frames.");
            }
            TimeBase clock = new(1, 1000);
            await using VideoPresentationReader reader = new(ReadAsync, new(new(start, clock), new(end, clock)),
                VideoIntervalAlignment.CompleteFrames, new(new(0, clock), new(10, clock)));
            Assert.Null(await reader.ReadFrameAsync());
            Assert.Null(await reader.ReadFrameAsync());
            Assert.Equal(0, reads);
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
