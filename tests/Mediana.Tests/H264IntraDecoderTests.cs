using System.Buffers.Binary;
using System.Security.Cryptography;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264IntraDecoderTests
    {
        [Theory]
        [InlineData("avc-tail", ".mov", 0, "8df41101ddfc3feb7ad3b4f8f7ac3d94815c0745b0dac7ca4d2d2c174685958c")]
        [InlineData("avc-tail", ".mov", 5, "dcded925869f945a85a604c356348a2f194f0d9ed5b7808b73222ee7307dca3d")]
        [InlineData("avc-cropped", ".mp4", 0, "fe331966ff4a7fb750238e45a5bd01670d3c284dd273208e3312296a48acd472")]
        public async Task BaselineIdrMatchesIndependentFilteredYuvSampleForSample(string name, string extension, int packetIndex, string hash)
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync(name + extension, packetIndex);
            byte[] expected = await Mp4TestData.ReadAsync($"{name}.frame{packetIndex}.yuv");
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(expected)));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture actual = H264IntraDecoder.Decode(nal, configuration, allocator))
            {
                Assert.Equal(80, actual.Width);
                Assert.Equal(48, actual.Height);
                int offset = 0;
                for (int plane = 0; plane < 3; plane++)
                {
                    int subsampling = plane == 0 ? 1 : 2;
                    int width = actual.Sequence.Width / subsampling;
                    int height = actual.Sequence.Height / subsampling;
                    int cropX = actual.Sequence.CropLeft / subsampling;
                    int cropY = actual.Sequence.CropTop / subsampling;
                    for (int row = 0; row < height; row++)
                    {
                        byte[] decoded = actual.Plane(plane).Slice((cropY + row) * actual.Stride(plane) + cropX, width).ToArray();
                        Assert.Equal(expected.AsSpan(offset, width).ToArray(), decoded);
                        offset += width;
                    }
                }
                Assert.Equal(expected.Length, offset);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RejectsUnsupportedCabacAndReleasesStorageAfterTruncatedSlices()
        {
            (H264Configuration high, byte[] cabac) = await ReadIdrAsync("avc-aac.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<NotSupportedException>(() => H264IntraDecoder.Decode(cabac, high, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            (H264Configuration baseline, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            foreach (int length in new[] { 0, 1, 2, 3, nal.Length / 2, nal.Length - 1 })
            {
                Assert.Throws<InvalidDataException>(() => H264IntraDecoder.Decode(nal.AsSpan(0, length), baseline, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task ChecksCancellationAndPictureBudget()
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            MediaBufferAllocator allocator = new(32);
            Assert.Throws<MediaMemoryLimitException>(() => H264IntraDecoder.Decode(nal, configuration, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => H264IntraDecoder.Decode(nal, configuration, allocator, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MutatedSlicesRemainBoundedAndReleaseTheirPictureStorage()
        {
            (H264Configuration configuration, byte[] nal) = await ReadIdrAsync("avc-tail.mov");
            MediaBufferAllocator allocator = new(1024 * 1024);
            Random random = new(264);
            for (int iteration = 0; iteration < 128; iteration++)
            {
                byte[] mutated = (byte[])nal.Clone();
                mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                Exception? error = Record.Exception(() =>
                {
                    using H264Picture picture = H264IntraDecoder.Decode(mutated, configuration, allocator);
                });
                Assert.True(error is null or InvalidDataException or NotSupportedException, error?.ToString());
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static async Task<(H264Configuration Configuration, byte[] Nal)> ReadIdrAsync(string name, int packetIndex = 0)
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(name));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, new H264DecoderOptions());
            for (int index = 0; ; index++)
            {
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                if (index < packetIndex)
                {
                    continue;
                }
                ReadOnlySpan<byte> remaining = packet.Data.Span;
                while (!remaining.IsEmpty)
                {
                    int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(remaining));
                    ReadOnlySpan<byte> nal = remaining.Slice(4, length);
                    if ((nal[0] & 31) == 5)
                    {
                        return (configuration, nal.ToArray());
                    }
                    remaining = remaining[(4 + length)..];
                }
                throw new InvalidDataException("The fixture packet has no IDR slice.");
            }
        }
    }
}
