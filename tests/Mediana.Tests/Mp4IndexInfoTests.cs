// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp4IndexInfoTests
    {
        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-tail.mov")]
        [InlineData("avc-mp3-22.mp4")]
        [InlineData("avc-mp3-44.mp4")]
        public async Task RealTrackTotalsMatchIndependentPacketsInBothTimelineModes(string file) =>
            await Mp4IndexInfoTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WideHeadersAndOffsetsRetainOriginalDeclarationsAndIndexClock(bool wide)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            if (wide)
            {
                data = Mp4TestData.Use64BitChunkOffsets(Mp4TestData.Use64BitTimeHeaders(data));
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            ContainerInfo info;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                info = reader.Info;
                ContainerStreamInfo track = info.StreamInfo![0];
                Assert.Equal(new MediaDuration(1200, new(1, 1000)), track.DeclaredDuration);
                Assert.Equal(new MediaDuration(12288, new(1, 10240)), track.PacketIndex!.DecodeDuration);
                Assert.Same(reader.Tracks[0].PacketIndex, track.PacketIndex);
                Assert.Equal(reader.Tracks[0].Stream.CodedDuration, track.PacketIndex.DecodeDuration);
            }
            Assert.Equal(6, info.StreamInfo![0].PacketIndex!.PacketCount);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0U)]
        [InlineData(uint.MaxValue)]
        public async Task HeaderZeroAndUnknownDurationsDoNotReplaceIndexedDuration(uint declaration)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            Mp4TestData.SetUInt32(data, "mvhd", 16, declaration);
            Mp4TestData.SetUInt32(data, "tkhd", 20, declaration);
            Mp4TestData.SetUInt32(data, "mdhd", 16, declaration);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                if (declaration == 0)
                {
                    Assert.Equal(0, reader.Info.DeclaredDuration!.Value.Value);
                    Assert.Equal(0, reader.Info.StreamInfo![0].DeclaredDuration!.Value.Value);
                    Assert.Equal(0, reader.Tracks[0].MediaDuration!.Value.Value);
                }
                else
                {
                    Assert.Null(reader.Info.DeclaredDuration);
                    Assert.Null(reader.Info.StreamInfo![0].DeclaredDuration);
                    Assert.Null(reader.Tracks[0].MediaDuration);
                }
                Assert.Equal(new MediaDuration(12288, new(1, 10240)), reader.Streams[0].CodedDuration);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0U)]
        [InlineData(2048U)]
        [InlineData(uint.MaxValue)]
        public async Task IndexDurationUsesEveryRunWithoutTruncatingToThirtyTwoBits(uint duration)
        {
            byte[] data = Mp4SampleTableTestData.WithTimingRuns(await Mp4TestData.ReadAsync("avc-tail.mov"), (6, duration));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                ContainerPacketIndexInfo facts = reader.Info.StreamInfo![0].PacketIndex!;
                Assert.Equal(new MediaDuration(6L * duration, new(1, 10240)), facts.DecodeDuration);
                Assert.Equal(facts.DecodeDuration, reader.Streams[0].CodedDuration);
                Assert.Equal(12288, reader.Tracks[0].MediaDuration!.Value.Value);
                Assert.Equal(6, facts.PacketCount);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task VariableDurationsAndConstantSampleSizesUseTheValidatedIndex()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            data = Mp4SampleTableTestData.WithTimingRuns(data, (2, 1001), (4, 3000));
            data = Mp4SampleTableTestData.WithConstantSampleSize(data, 3);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                ContainerPacketIndexInfo facts = reader.Info.StreamInfo![0].PacketIndex!;
                Assert.Equal(18, facts.PayloadBytes);
                Assert.Equal(6, facts.PacketCount);
                Assert.Equal(new MediaDuration(14002, new(1, 10240)), facts.DecodeDuration);
                Assert.Equal(new MediaDuration(1200, new(1, 1000)), reader.Info.StreamInfo[0].DeclaredDuration);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
