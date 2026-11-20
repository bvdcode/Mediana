// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Codecs.Pnm;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class PnmDecoderTests
    {
        [Fact]
        public async Task PlainAndBinaryVariantsDecodeTheSamePixels()
        {
            byte[][] sources =
            [
                "P1\n# bitmap\n3 2\n010\n101"u8.ToArray(),
                [.. "P4\n3 2\n"u8, 0x5f, 0xa0],
                "P2\n3 2\n15\n15 0 15\n0 15 0\n"u8.ToArray(),
                [.. "P5\n3 2\n255\n"u8, 255, 0, 255, 0, 255, 0],
                [.. "P5\n3 2\n1023\n"u8, 3, 255, 0, 0, 3, 255, 0, 0, 3, 255, 0, 0],
                "P3\n3 2\n1\n1 1 1 0 0 0 1 1 1\n0 0 0 1 1 1 0 0 0\n"u8.ToArray(),
                [.. "P6\n3 2\n255\n"u8, 255, 255, 255, 0, 0, 0, 255, 255, 255, 0, 0, 0, 255, 255, 255, 0, 0, 0]
            ];
            byte[] expected = [255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255,
                0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255];
            MediaBufferAllocator allocator = new(expected.Length);
            PnmImageDecoder decoder = new();
            ImageDecodeContext registry = new([decoder]);
            foreach (byte[] source in sources)
            {
                using (VideoFrame image = await registry.DecodeAsync(source, new(), allocator))
                {
                    Assert.Equal(expected, image.Data.ToArray());
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                ImageInfo info = await decoder.ReadInfoAsync(source);
                Assert.Equal(3, info.Width); Assert.Equal(2, info.Height); Assert.Equal(1, info.PageCount);
            }
        }

        [Fact]
        public async Task BinaryRasterBytesAreNotConsumedAsWhitespaceOrCommentsAndPagesAreSelectable()
        {
            byte[] encoded = [.. "P6\n1 1\n255\n"u8, 10, 32, 35, .. "P5\n1 1\n65535\n"u8, 0x80, 0];
            PnmImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(8);
            using VideoFrame first = await decoder.DecodeAsync(encoded, new(), allocator);
            using VideoFrame second = await decoder.DecodeAsync(encoded, new() { PageIndex = 1 }, allocator);
            Assert.Equal(new byte[] { 10, 32, 35, 255 }, first.Data.ToArray());
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, second.Data.ToArray());
            Assert.Equal(TransferFunction.Bt709, first.Format.Color.Transfer);
            ImageInfo info = await decoder.ReadInfoAsync(encoded);
            Assert.Equal(2, info.PageCount); Assert.Equal(2, info.Metadata.Children.Count);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await decoder.DecodeAsync(encoded, new() { PageIndex = 2 }, allocator));
        }

        [Theory]
        [InlineData("P2\n0 1\n255\n0")]
        [InlineData("P2\n1 1\n65536\n0")]
        [InlineData("P2\n1 1\n10\n11")]
        [InlineData("P1\n1 1\n2")]
        [InlineData("P1\n1 1\n00")]
        [InlineData("P2\n1 1\n255\n")]
        [InlineData("P2\n1 1\n255\n0 1")]
        [InlineData("P6\n2147483647 2147483647\n65535\n")]
        [InlineData("P6\n99999999999 1\n255\n")]
        [InlineData("P6\n1a 1\n255\nabc")]
        public void InvalidHeadersAndPlainDataFailWithoutLeaking(string text)
        {
            MediaBufferAllocator allocator = new(1024);
            Assert.Throws<InvalidDataException>(() => new PnmImageDecoder().Decode(Encoding.ASCII.GetBytes(text), new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TruncatedAndOutOfRangeBinaryDataFailAndCancellationIsObserved()
        {
            PnmImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024);
            byte[] valid = [.. "P6\n1 1\n1023\n"u8, 3, 255, 0, 0, 2, 0];
            for (int size = 0; size < valid.Length; size++)
            {
                Assert.Throws<InvalidDataException>(() => decoder.Decode(valid[..size], new(), allocator));
            }
            byte[] invalid = [.. "P5\n1 1\n3\n"u8, 4];
            Assert.Throws<InvalidDataException>(() => decoder.Decode(invalid, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(valid, new(), allocator, new(true)));
            Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(valid, new(), new(3)));
        }
    }
}
