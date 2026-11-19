// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Tiff;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Xunit;

namespace Mediana.Tests
{
    public class TiffDeflateTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(32946)]
        public void IndependentStripsReuseDecoderWithoutSharingHistory(int compression)
        {
            byte[] first = [0, 1, 2, 3, 4, 5, 6, 7];
            byte[] second = [255, 200, 150, 100, 50, 20, 10, 0];
            byte[] input = Create(Compress(first), Compress(second), compression);
            MediaBufferAllocator allocator = new(1024);
            TiffImageDecoder decoder = new();
            for (int iteration = 0; iteration < 3; iteration++)
            {
                using VideoFrame frame = decoder.Decode(input, new(), allocator);
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(new byte[] { first[x], first[x], first[x], 255 }, frame.GetRow(0, 0).Slice(x * 4, 4).ToArray());
                    Assert.Equal(new byte[] { second[x], second[x], second[x], 255 }, frame.GetRow(0, 1).Slice(x * 4, 4).ToArray());
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void IncompleteOversizedAndCorruptSecondStripsReleaseEveryBuffer()
        {
            byte[] good = Compress(new byte[8]);
            byte[] corrupt = good.ToArray();
            corrupt[^1] ^= 1;
            byte[][] invalid = [good[..^1], corrupt, Compress(new byte[7]), Compress(new byte[9]), []];
            MediaBufferAllocator allocator = new(1024);
            foreach (byte[] block in invalid)
            {
                byte[] input = Create(good, block, 8);
                Assert.Throws<InvalidDataException>(() => new TiffImageDecoder().Decode(input, new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static byte[] Compress(byte[] samples)
        {
            using MemoryStream result = new();
            using (ZLibStream compressor = new(result, CompressionLevel.Optimal, true)) { compressor.Write(samples); }
            return result.ToArray();
        }

        private static byte[] Create(byte[] first, byte[] second, int compression)
        {
            const int entries = 9;
            const int offsets = 8 + 2 + entries * 12 + 4;
            const int counts = offsets + 8;
            const int payload = counts + 8;
            using MemoryStream result = new();
            using BinaryWriter writer = new(result);
            writer.Write((ushort)0x4949);
            writer.Write((ushort)42);
            writer.Write(8);
            writer.Write((ushort)entries);
            void Field(ushort tag, ushort type, int count, int value)
            {
                writer.Write(tag); writer.Write(type); writer.Write(count); writer.Write(value);
            }
            Field(256, 4, 1, 8);
            Field(257, 4, 1, 2);
            Field(258, 3, 1, 8);
            Field(259, 3, 1, compression);
            Field(262, 3, 1, 1);
            Field(273, 4, 2, offsets);
            Field(277, 3, 1, 1);
            Field(278, 4, 1, 1);
            Field(279, 4, 2, counts);
            writer.Write(0);
            writer.Write(payload); writer.Write(payload + first.Length);
            writer.Write(first.Length); writer.Write(second.Length);
            writer.Write(first); writer.Write(second);
            return result.ToArray();
        }
    }
}
