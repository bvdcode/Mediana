// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class IndexedCopyPlannerTests
    {
        [Theory]
        [InlineData(2, 4, 10, 10)]
        [InlineData(10, 12, 50, 10)]
        public async Task IndependentVideoBoundariesReadOnlyTwoRestartPacketsAndCopyExactPayloads(int start, int end, long first, long count)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long baseline = allocator.AllocatedBytes;
            source.Reads.Clear();
            MediaReadBudget budget = new();
            IndexedPacketRange range = await IndexedCopyPlanner.CreateAsync(reader, 0, new(new(start, new(1, 1)), new(end, new(1, 1))),
                new H264DecoderFactory().CreateRandomAccessInspector(reader.Streams[0]), budget);
            Assert.Equal(new IndexedPacketRange(first, count), range);
            Assert.Equal(end == 12 ? 1 : 2, budget.PacketReads);
            Assert.Equal(budget.PacketReads, source.Reads.Count);
            await using IndexedCopySource copy = new(reader, 0, 17, range.FirstPacket, range.PacketCount, budget);
            Assert.Null(copy.Stream.CodedDuration);
            for (long index = 0; index < count; index++)
            {
                using MediaPacket original = await reader.ReadPacketAtAsync(0, checked((int)(first + index)));
                using MediaPacket packet = (await copy.ReadPacketAsync())!;
                Assert.Equal(17, packet.StreamIndex);
                Assert.Equal(original.PresentationTime, packet.PresentationTime);
                Assert.Equal(original.DecodeTime, packet.DecodeTime);
                Assert.Equal(original.Data.ToArray(), packet.Data.ToArray());
            }
            Assert.Null(await copy.ReadPacketAsync());
            long reads = budget.PacketReads;
            Assert.Null(await copy.ReadPacketAsync());
            Assert.Equal(reads, budget.PacketReads);
            Assert.Equal(baseline, allocator.AllocatedBytes);
            using MediaPacket firstSequential = (await reader.ReadPacketAsync())!;
            Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, firstSequential.PresentationTime);
        }

        [Theory]
        [InlineData(1, 4)]
        [InlineData(2, 3)]
        public async Task MidGopRequestsRejectWithoutRoundingOrReadingTheWholeFile(int start, int end)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long baseline = allocator.AllocatedBytes;
            source.Reads.Clear();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await IndexedCopyPlanner.CreateAsync(reader, 0,
                new(new(start, new(1, 1)), new(end, new(1, 1))), new H264DecoderFactory().CreateRandomAccessInspector(reader.Streams[0])));
            Assert.InRange(source.Reads.Count, 0, 2);
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SelectedAudioPacketsNeedNoRestartPayloadAndSharedCopyReadsRemainLimited()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-aac.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            MediaTimestamp start = reader.GetSampleInfo(1, 5).PresentationTime;
            MediaTimestamp end = reader.GetSampleInfo(1, 9).PresentationTime;
            source.Reads.Clear();
            IndexedPacketRange range = await IndexedCopyPlanner.CreateAsync(reader, 1, new(start, end));
            Assert.Equal(new IndexedPacketRange(5, 4), range);
            Assert.Empty(source.Reads);
            MediaReadBudget shared = new(maximumPackets: 3);
            await using IndexedCopySource first = new(reader, 1, 7, 5, 4, shared);
            await using IndexedCopySource second = new(reader, 1, 13, 5, 4, shared);
            using MediaPacket a = (await first.ReadPacketAsync())!;
            using MediaPacket b = (await second.ReadPacketAsync())!;
            using MediaPacket c = (await first.ReadPacketAsync())!;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using MediaPacket? packet = await second.ReadPacketAsync(); });
            Assert.Equal(3, shared.PacketReads);
        }
    }
}
