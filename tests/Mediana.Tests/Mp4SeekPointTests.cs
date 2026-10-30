// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp4SeekPointTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", Mp4TimestampMode.Media)]
        [InlineData("avc-aac.mp4", Mp4TimestampMode.LinearMovie)]
        [InlineData("avc-tail.mov", Mp4TimestampMode.LinearMovie)]
        [InlineData("avc-multislice-cabac.mp4", Mp4TimestampMode.Media)]
        [InlineData("avc-multislice-cabac.mp4", Mp4TimestampMode.LinearMovie)]
        public async Task CandidateLookupMatchesAllSampleMetadataWithoutReadingMediaOrMovingCursors(string name, Mp4TimestampMode mode)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(name));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode });
            await VerifyCandidatesAsync(reader, source);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SignedCompositionTimesAndFractionalMovieDelaysUseExactTargetComparisons(bool signedComposition)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4");
            if (signedComposition)
            {
                int offset = Mp4TestData.PayloadOffset(data, "ctts");
                data[offset] = 1;
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));
                for (int index = 0; index < count; index++)
                {
                    int position = offset + 12 + index * 8;
                    int value = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
                    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(position), value - 8192);
                }
            }
            data = Mp4EditTestData.Replace(data, false, (1, -1, 65536), (1600, 7, 65536));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            await VerifyCandidatesAsync(reader, source);
        }

        [Fact]
        public async Task InvalidBoundsCancellationAndDisposalDoNotReadMedia()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            source.Reads.Clear();
            MediaTimestamp target = new(1, new(1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPacketCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.FindSeekPoint(0, target, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.FindSeekPoint(0, target, long.MaxValue));
            Assert.Throws<OperationCanceledException>(() => reader.FindSeekPoint(0, target, 0, new(true)));
            Assert.Throws<ArgumentException>(() => reader.FindSeekPoint(0, default, 0));
            Assert.Null(reader.FindSeekPoint(0, target, 0));
            await reader.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => reader.GetPacketCount(0));
            Assert.Throws<ObjectDisposedException>(() => reader.FindSeekPoint(0, target, 0));
            Assert.Empty(source.Reads);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task VerifyCandidatesAsync(Mp4Reader reader, TrackingRandomAccessSource source)
        {
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            source.Reads.Clear();
            IIndexedContainerReader indexed = reader;
            for (int stream = 0; stream < reader.Streams.Count; stream++)
            {
                int count = reader.Tracks[stream].SampleCount;
                Assert.Equal(count, indexed.GetPacketCount(stream));
                Mp4SampleInfo[] samples = Enumerable.Range(0, count).Select(index => reader.GetSampleInfo(stream, index)).ToArray();
                foreach (MediaTimestamp target in new MediaTimestamp[]
                {
                    new(-1000, new(1, 1000)), new(0, new(1, 1000)), new(333, new(1, 1000)),
                    new(999, new(1, 1000)), new(2000, new(1, 1000)), new(2999999999, new(1, 1000000000)),
                    new(long.MaxValue, new(int.MaxValue, 1)), new(long.MinValue, new(int.MaxValue, 1))
                })
                {
                    for (int before = 0; before <= count; before++)
                    {
                        ContainerSeekPoint? point = indexed.FindSeekPoint(stream, target, before);
                        if (before == 0)
                        {
                            Assert.Null(point);
                            continue;
                        }
                        int expected = 0;
                        foreach (Mp4SampleInfo sample in samples[..before])
                        {
                            if (sample.IsSync && sample.PresentationTime.CompareTo(target) <= 0)
                            {
                                expected = sample.SampleIndex;
                            }
                        }
                        Assert.Equal(new ContainerSeekPoint(stream, expected, samples[expected].PresentationTime), point);
                    }
                }
            }
            Assert.Empty(source.Reads);
            using MediaPacket next = (await reader.ReadPacketAsync())!;
            Assert.NotEqual((first.StreamIndex, first.DecodeTime), (next.StreamIndex, next.DecodeTime));
        }
    }
}
