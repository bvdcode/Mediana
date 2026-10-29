// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsPlanningTests
    {
        [Fact]
        public async Task RealClosedGopsProduceStableIdrBoundariesAndExactVideoDurations() =>
            await HlsPlanTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Theory]
        [InlineData(1, 1000000, 8)]
        [InlineData(1, 1, 8)]
        [InlineData(100, 1, 1)]
        public async Task TargetIntervalSelectsIdrsRatherThanInventingBoundaries(int numerator, int denominator, int count)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            HlsPlanningOptions options = new() { TargetDuration = new(numerator, new TimeBase(1, denominator)) };
            Mp4AvcSegmentPlan plan = await Mp4AvcSegmentPlan.CreateAsync(reader, 0, options);
            Assert.Equal(count, plan.Segments.Count);
            Assert.Equal(36, plan.Segments.Sum(segment => segment.SampleCount));
            Assert.Equal(73728, plan.Segments.Sum(segment => segment.Duration.Value));
        }

        [Fact]
        public async Task FalseSyncFlagIsNotAnIdrAndPlanningOnlyReadsCandidatePayloads()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            Mp4TestData.SetUInt32(data, "stss", 8, 2);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 11);
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            Mp4SampleInfo[] candidates = [reader.GetSampleInfo(0, 0), reader.GetSampleInfo(0, 1), reader.GetSampleInfo(0, 5)];
            source.Reads.Clear();
            Mp4AvcSegmentPlan plan = await Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                new() { TargetDuration = new(1, new TimeBase(1, 2)) });
            Assert.Equal(new[] { 0, 5 }, plan.Segments.Select(segment => segment.FirstSample));
            Assert.All(source.Reads, range => Assert.Contains(candidates,
                sample => range.Offset >= sample.Offset && range.Offset + range.Length <= sample.Offset + sample.Size));
            Assert.Equal(candidates.Sum(sample => sample.Size), source.Reads.Sum(range => range.Length));
        }

        [Fact]
        public async Task MissingInitialIdrIsRejectedWithoutSkippingVideo()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            Mp4SampleInfo first = reader.GetSampleInfo(0, 0);
            int offset = checked((int)first.Offset);
            bool changed = false;
            while (offset < first.Offset + first.Size)
            {
                int size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
                offset += 4;
                if ((data[offset] & 31) == 5)
                {
                    data[offset] = 0x61;
                    changed = true;
                    break;
                }
                offset += size;
            }
            Assert.True(changed);
            long allocated = allocator.AllocatedBytes;
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0).AsTask());
            Assert.Equal(allocated, allocator.AllocatedBytes);
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Assert.Equal(first.DecodeTime, packet.DecodeTime);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task UnrepresentableVideoWindowsAreRejected(int shape)
        {
            byte[] data = await Mp4TestData.ReadAsync();
            switch (shape)
            {
                case 0:
                    Mp4TestData.SetUInt32(data, "stts", 12, 0);
                    break;
                case 1:
                    Mp4TestData.SetUInt32(data, "ctts", 12, 16384);
                    break;
                case 2:
                    Mp4TestData.SetUInt32(data, "ctts", 20, 18432);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            long allocated = allocator.AllocatedBytes;
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                new() { TargetDuration = new(1, new TimeBase(1, 2)) }).AsTask());
            Assert.Equal(allocated, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SegmentLimitFailureReleasesPacketsAndPreservesCursor()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            long allocated = allocator.AllocatedBytes;
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                new() { TargetDuration = new(1, new TimeBase(1, 1)), MaximumSegments = 1 }).AsTask());
            Assert.Equal(allocated, allocator.AllocatedBytes);
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(0, first.PresentationTime!.Value.Value);
        }

        [Fact]
        public async Task CancelledCandidateReadCanBeRetriedWithoutLosingState()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"), 13);
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            long allocated = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            source.AfterRead = cancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Mp4AvcSegmentPlan.CreateAsync(reader, 0, cancellationToken: cancellation.Token).AsTask());
            Assert.Equal(allocated, allocator.AllocatedBytes);
            source.AfterRead = null;
            Mp4AvcSegmentPlan plan = await Mp4AvcSegmentPlan.CreateAsync(reader, 0);
            Assert.Equal(2, plan.Segments.Count);
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(0, first.PresentationTime!.Value.Value);
        }

        [Fact]
        public async Task InvalidOptionsStreamsAndTimestampModeFailBeforeReadingMedia()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            await using Mp4Reader reader = await OpenAsync(source, allocator);
            int reads = source.Reads.Count;
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, -1).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 2).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 1).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                new() { MaximumSegments = 0 }).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                new() { TargetDuration = new(0, new TimeBase(1, 1)) }).AsTask());
            await Assert.ThrowsAsync<OperationCanceledException>(() => Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                cancellationToken: new(true)).AsTask());
            Assert.Equal(reads, source.Reads.Count);
            await using Mp4Reader mediaReader = await Mp4Reader.OpenAsync(source, allocator);
            reads = source.Reads.Count;
            await Assert.ThrowsAsync<ArgumentException>(() => Mp4AvcSegmentPlan.CreateAsync(mediaReader, 0).AsTask());
            Assert.Equal(reads, source.Reads.Count);
        }

        private static ValueTask<Mp4Reader> OpenAsync(TrackingRandomAccessSource source, MediaBufferAllocator allocator) =>
            Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
    }
}
