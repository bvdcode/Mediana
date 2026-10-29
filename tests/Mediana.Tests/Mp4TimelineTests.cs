// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp4TimelineTests
    {
        private static Mp4ReadOptions MovieOptions => new() { TimestampMode = Mp4TimestampMode.LinearMovie };

        [Fact]
        public async Task RealMoviePreservesAllCompressedPacketsWithTranslatedAndMergedTimes() =>
            await Mp4TimelineTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Theory]
        [InlineData(false, 1, 5, 256000, 131)]
        [InlineData(true, 1, 5, 256000, 131)]
        [InlineData(false, 1000, 5, 10240, 10235)]
        [InlineData(true, 4294967297, 4294967298, 256000, 992137445582)]
        public async Task EmptyEditDelayIsExactAndOriginalTrackDeclarationsRemainUnchanged(bool wide,
            long delay, long origin, int denominator, long firstTime)
        {
            byte[] data = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), wide,
                (delay, -1, 65536), (1200, origin, 65536));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, MovieOptions))
            {
                Assert.Equal(new TimeBase(1, 10240), reader.Tracks[0].Stream.TimeBase);
                Assert.Equal(new TimeBase(1, denominator), reader.Streams[0].TimeBase);
                Assert.Equal(origin, reader.Tracks[0].Edits[1].MediaTime);
                Assert.Equal(delay, reader.Tracks[0].Edits[0].Duration.Value);
                Assert.Equal(reader.Tracks[0].Stream.Configuration.ToArray(), reader.Streams[0].Configuration.ToArray());
                Assert.Equal(reader.Tracks[0].Stream.DisplayMatrix, reader.Streams[0].DisplayMatrix);
                for (int index = 0; index < 6; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    long expected = firstTime + (long)index * 2048 * (denominator / 10240);
                    Assert.Equal(expected, packet.DecodeTime!.Value.Value);
                    Assert.Equal(expected, packet.PresentationTime!.Value.Value);
                    Assert.Equal(new MediaDuration(2048L * (denominator / 10240), new(1, denominator)), packet.Duration);
                }
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task MissingEditsPreserveIdentityEvenWithDifferentMovieTimeScale()
        {
            byte[] data = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), false);
            Mp4TestData.SetUInt32(data, "mvhd", 12, int.MaxValue);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, MovieOptions);
            Assert.Empty(reader.Tracks[0].Edits);
            Assert.Same(reader.Tracks[0].Stream, reader.Streams[0]);
            using MediaPacket packet = (await reader.ReadPacketAsync())!;
            Assert.Equal(new MediaTimestamp(0, new(1, 10240)), packet.DecodeTime);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task NonlinearEditsAreExplicitlyRejectedOnlyInMovieMode(int shape)
        {
            (long Duration, long MediaTime, int Rate)[] edits = shape switch
            {
                0 => [(1200, -1, 65536)],
                1 => [(600, 0, 65536), (600, 0, 65536)],
                2 => [(1200, 0, 65536), (1, -1, 65536)],
                3 => [(1, -1, 65536), (1, -1, 65536), (1200, 0, 65536)],
                4 => [(1200, 0, 0)],
                5 => [(1200, 0, 131072)],
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            byte[] data = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), false, edits);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator, MovieOptions));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(Mp4TimestampMode.Media, reader.TimestampMode);
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(0, packet.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnrepresentableExactTimeBaseFailsWithoutRetainingMetadata()
        {
            byte[] data = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), false,
                (1, -1, 65536), (1200, 0, 65536));
            Mp4TestData.SetUInt32(data, "mvhd", 12, int.MaxValue);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(source, allocator, MovieOptions));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task TimestampOverflowFailsBeforeReadingOrAllocatingPayload()
        {
            byte[] data = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), true,
                (1, -1, 65536), (1200, long.MaxValue, 65536));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, MovieOptions))
            {
                int reads = source.Reads.Count;
                long allocated = allocator.AllocatedBytes;
                await Assert.ThrowsAsync<OverflowException>(async () => await reader.ReadPacketAsync());
                Assert.Equal(reads, source.Reads.Count);
                Assert.Equal(allocated, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancelledMovieReadCanRetryTheSameTranslatedPacket()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 17);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, MovieOptions))
            {
                long allocated = allocator.AllocatedBytes;
                using CancellationTokenSource cancellation = new();
                source.AfterRead = cancellation.Cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPacketAsync(cancellation.Token));
                Assert.Equal(allocated, allocator.AllocatedBytes);
                source.AfterRead = null;
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(0, packet.StreamIndex);
                Assert.Equal(0, packet.PresentationTime!.Value.Value);
                Assert.Equal(-4096, packet.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FactoryUsesTheSelectedTimelineAndInvalidModesDoNotReadSource()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            Mp4ReadOptions invalid = new() { TimestampMode = (Mp4TimestampMode)123 };
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Mp4Reader.OpenAsync(source, allocator, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Mp4ReaderFactory(invalid));
            Assert.Empty(source.Reads);
            Mp4ReaderFactory factory = new(MovieOptions);
            await using (IContainerReader reader = await factory.OpenAsync(source, allocator))
            {
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(-4096, packet.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
