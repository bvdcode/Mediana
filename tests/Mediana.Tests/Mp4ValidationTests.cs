using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4ValidationTests
    {
        [Theory]
        [InlineData("stts", 8, 0)]
        [InlineData("stts", 4, 10000)]
        [InlineData("stco", 8, 0)]
        [InlineData("stsc", 8, 0)]
        [InlineData("stsc", 12, 10000)]
        [InlineData("stss", 8, 0)]
        [InlineData("mdhd", 12, 0)]
        public async Task InvalidTablesAreRejectedWithoutLeakingMetadata(string box, int field, uint value)
        {
            byte[] data = await Mp4TestData.ReadAsync();
            Mp4TestData.SetUInt32(data, box, field, value);
            await AssertOpenFailureAsync<InvalidDataException>(data);
        }

        [Theory]
        [InlineData("stsz", 8, uint.MaxValue)]
        [InlineData("stsd", 4, 2)]
        [InlineData("stsc", 16, 2)]
        [InlineData("dref", 4, 2)]
        [InlineData("ctts", 0, 0x02000000)]
        [InlineData("hdlr", 8, 0x6D657461)]
        [InlineData("mdhd", 12, uint.MaxValue)]
        public async Task UnsupportedConfigurationsAreExplicit(string box, int field, uint value)
        {
            byte[] data = await Mp4TestData.ReadAsync();
            Mp4TestData.SetUInt32(data, box, field, value);
            await AssertOpenFailureAsync<NotSupportedException>(data);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(20)]
        [InlineData(500)]
        public async Task TruncatedFilesAreRejected(int length)
        {
            byte[] data = await Mp4TestData.ReadAsync();
            await AssertOpenFailureAsync<InvalidDataException>(data[..length]);
        }

        [Fact]
        public async Task MetadataMemoryAndWorkLimitsReleaseAllStorage()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            await AssertOpenFailureAsync<MediaMemoryLimitException>(data, budget: 16);
            await AssertOpenFailureAsync<NotSupportedException>(data, new Mp4ReadOptions { MaxMetadataBytes = 16 });
            await AssertOpenFailureAsync<NotSupportedException>(data, new Mp4ReadOptions { MaxBoxes = 1 });
            await AssertOpenFailureAsync<NotSupportedException>(data, new Mp4ReadOptions { MaxTracks = 1 });
            await AssertOpenFailureAsync<NotSupportedException>(data, new Mp4ReadOptions { MaxSamplesPerTrack = 1 });
            await AssertOpenFailureAsync<NotSupportedException>(data, new Mp4ReadOptions { MaxCodecConfigurationBytes = 1 });
        }

        [Fact]
        public async Task FragmentedMovieIsRejectedBeforePacketDelivery()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            byte[] fragmentHeader = [0, 0, 0, 8, .. "moof"u8];
            await AssertOpenFailureAsync<NotSupportedException>([.. data, .. fragmentHeader]);
        }

        [Fact]
        public async Task ExtendedAndToEndBoxesDoNotChangeSampleLocations()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            byte[] extended = [0, 0, 0, 1, .. "free"u8, 0, 0, 0, 0, 0, 0, 0, 16];
            byte[] toEnd = [0, 0, 0, 0, .. "free"u8, 7, 8, 9];
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new([.. data, .. extended, .. toEnd]);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            int count = 0;
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                packet.Dispose();
                count++;
            }
            Assert.Equal(6, count);
        }

        [Fact]
        public async Task InvalidExtendedSizeAndDuplicateMovieAreRejected()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            byte[] extended = [0, 0, 0, 1, .. "free"u8, 0, 0, 0, 0, 0, 0, 0, 15];
            await AssertOpenFailureAsync<InvalidDataException>([.. data, .. extended]);
            int movie = Mp4TestData.PayloadOffset(data, "moov") - 8;
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(movie, 4));
            await AssertOpenFailureAsync<InvalidDataException>([.. data, .. data.AsSpan(movie, length)]);
        }

        [Fact]
        public async Task SignedCompositionOffsetsPreserveNegativePresentationTimes()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            int composition = Mp4TestData.PayloadOffset(data, "ctts");
            data[composition] = 1;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(composition + 4, 4));
            for (int index = 0; index < count; index++)
            {
                int offset = composition + 12 + index * 8;
                int original = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), original - 8192);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using MediaPacket? packet = await reader.ReadPacketAsync();
            Assert.NotNull(packet);
            Assert.Equal(-4096, packet.PresentationTime!.Value.Value);
            Assert.Equal(0, packet.DecodeTime!.Value.Value);
        }

        [Fact]
        public async Task CancellationDuringOpenReleasesMetadataAndLeavesSourceOpen()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 3);
            using CancellationTokenSource cancellation = new();
            int moviePayload = Mp4TestData.PayloadOffset(data, "moov");
            source.AfterRead = () =>
            {
                if (source.Reads[^1].Offset >= moviePayload)
                {
                    cancellation.Cancel();
                }
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Mp4Reader.OpenAsync(source, allocator, cancellationToken: cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        private static async Task AssertOpenFailureAsync<TException>(byte[] data,
            Mp4ReadOptions? options = null, long budget = 1024 * 1024) where TException : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            await using TrackingRandomAccessSource source = new(data);
            await Assert.ThrowsAsync<TException>(async () => await Mp4Reader.OpenAsync(source, allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
