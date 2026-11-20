// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Qoi;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class QoiDecoderTests
    {
        [Fact]
        public async Task IndependentEncoderPixelsMatchExactly()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "qoi-reference.qoi"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "qoi-reference.rgba"));
            MediaBufferAllocator allocator = new(expected.Length);
            ImageDecodeContext registry = new([new QoiImageDecoder()]);
            using VideoFrame decoded = await registry.DecodeAsync(encoded, new(), allocator);
            Assert.Equal(expected, decoded.Data.ToArray());
        }

        [Fact]
        public async Task EveryOpcodePreservesStraightPixelsAndColorMetadata()
        {
            byte[] encoded = Image([0xff, 10, 20, 30, 40, 0xc0, 12, 0xfe, 1, 2, 3, 0x76, 0xa3, 0x79], 6);
            byte[] expected = [10, 20, 30, 40, 10, 20, 30, 40, 10, 20, 30, 40, 1, 2, 3, 40, 2, 1, 3, 40, 4, 4, 7, 40];
            QoiImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(expected.Length);
            using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
            {
                Assert.Equal(expected, frame.Data.ToArray());
                Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
                Assert.Equal(expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            encoded[13] = 1;
            using VideoFrame linear = await decoder.DecodeAsync(encoded, new(), allocator);
            Assert.Equal(TransferFunction.Linear, linear.Format.Color.Transfer);
            ImageInfo info = await decoder.ReadInfoAsync(encoded);
            Assert.Equal(6, info.Width); Assert.Equal(1, info.Height); Assert.Equal("qoi", info.FormatId);
        }

        [Fact]
        public void IndexStartsTransparentAndDeltasWrap()
        {
            QoiImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024);
            using VideoFrame frame = decoder.Decode(Image([0, 0x40, 0xff, 255, 255, 255, 255, 0x7f], 4), new(), allocator);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 254, 254, 254, 0, 255, 255, 255, 255, 0, 0, 0, 255 }, frame.Data.ToArray());
        }

        [Fact]
        public void MalformedChunksAndBudgetFailuresReleaseOutput()
        {
            QoiImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(128);
            foreach (byte[] payload in new byte[][] { [], [0xff, 1, 2], [0xc2], [0xc0, 0xc0], [0, 0] })
            {
                Assert.Throws<InvalidDataException>(() => decoder.Decode(Image(payload, payload.Length == 2 && payload[0] == 0 ? 2 : 1), new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            byte[] valid = Image([0xc0], 1);
            for (int length = 0; length < valid.Length; length++)
            {
                Assert.Throws<InvalidDataException>(() => decoder.Decode(valid[..length], new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(valid, new(), new(3)));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(Image([0xc1], 2), new() { MaximumPixels = 1 }, allocator));
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(valid, new(), allocator, new(true)));
        }

        private static byte[] Image(byte[] payload, int width)
        {
            byte[] result = new byte[22 + payload.Length];
            "qoif"u8.CopyTo(result);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), width);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), 1);
            result[12] = 4; result[^1] = 1;
            payload.CopyTo(result, 14);
            return result;
        }
    }
}
