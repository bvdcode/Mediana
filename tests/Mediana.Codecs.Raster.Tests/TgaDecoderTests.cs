// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Codecs.Tga;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class TgaDecoderTests
    {
        [Fact]
        public async Task IndependentEncodingsMatchAcrossPixelTypesAndCompression()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "tga-manifest.json")));
            ImageDecodeContext context = new([new TgaImageDecoder()]);
            foreach (JsonElement item in manifest.RootElement.EnumerateArray())
            {
                byte[] data = await File.ReadAllBytesAsync(Path.Combine(root, item.GetProperty("file").GetString()!));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, item.GetProperty("pixels").GetString()!));
                MediaBufferAllocator allocator = new(expected.Length);
                using (VideoFrame frame = await context.DecodeAsync(data, new(), allocator)) { Assert.Equal(expected, frame.Data.ToArray()); }
                Assert.Equal(0, allocator.AllocatedBytes);
                ImageInfo info = await new TgaImageDecoder().ReadInfoAsync(data);
                Assert.Equal(37, info.Width); Assert.Equal(29, info.Height);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(48)]
        [InlineData(64)]
        [InlineData(128)]
        public void OriginsAndLegacyInterleavingMapToTopDownRows(int descriptor)
        {
            byte[] data = Header(2, 7, 24, descriptor, 2);
            List<byte> payload = [];
            int step = descriptor >> 6;
            int[] rows = step switch { 0 => [0, 1, 2, 3, 4, 5, 6], 1 => [0, 2, 4, 6, 1, 3, 5], 2 => [0, 4, 1, 5, 2, 6, 3], _ => throw new ArgumentOutOfRangeException(nameof(descriptor)) };
            foreach (int row in rows)
            {
                int y = (descriptor & 32) == 0 ? 6 - row : row;
                for (int column = 0; column < 2; column++)
                {
                    int x = (descriptor & 16) == 0 ? column : 1 - column;
                    payload.AddRange([0, (byte)(y * 40), (byte)(x * 80)]);
                }
            }
            MediaBufferAllocator allocator = new(56);
            using VideoFrame frame = new TgaImageDecoder().Decode((byte[])[.. data, .. payload], new(), allocator);
            for (int y = 0; y < 7; y++)
            {
                Assert.Equal(new byte[] { 0, (byte)(y * 40), 0, 255, 80, (byte)(y * 40), 0, 255 }, frame.GetRow(0, y).ToArray());
            }
        }

        [Fact]
        public void ExtensionDistinguishesStraightAssociatedAndIgnoredAlpha()
        {
            TgaImageDecoder decoder = new();
            byte[] pixels = [50, 25, 100, 128];
            byte[] encoded = [.. Header(1, 1, 32, 40, 2), .. pixels, .. new byte[495 + 26]];
            int extension = 22, footer = encoded.Length - 26;
            BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(extension), 495);
            BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(footer), extension);
            "TRUEVISION-XFILE.\0"u8.CopyTo(encoded.AsSpan(footer + 8));
            MediaBufferAllocator allocator = new(16);
            foreach (int alpha in new[] { 0, 1, 3, 4 })
            {
                encoded[extension + 494] = (byte)alpha;
                using VideoFrame frame = decoder.Decode(encoded, new(), allocator);
                byte[] expected = alpha switch
                {
                    0 or 1 => [100, 25, 50, 255],
                    3 => [100, 25, 50, 128],
                    4 => [199, 50, 100, 128],
                    _ => throw new ArgumentOutOfRangeException(nameof(alpha))
                };
                Assert.Equal(expected, frame.Data.ToArray());
            }
            encoded[extension + 494] = 2;
            Assert.Throws<NotSupportedException>(() => decoder.Decode(encoded, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RleTruncationInvalidPaletteIndicesAndOverflowingRunsReleaseStorage()
        {
            MediaBufferAllocator allocator = new(64);
            TgaImageDecoder decoder = new();
            byte[] header = Header(2, 1, 24, 32, 10);
            foreach (byte[] bytes in new byte[][] { [0x81, 1, 2], [0x82, 1, 2, 3], [1, 1, 2, 3] })
            {
                Assert.Throws<InvalidDataException>(() => decoder.Decode((byte[])[.. header, .. bytes], new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            byte[] palette = Header(1, 1, 8, 32, 1);
            palette[1] = 1; palette[3] = 8; palette[5] = 1; palette[7] = 24;
            Assert.Throws<InvalidDataException>(() => decoder.Decode((byte[])[.. palette, 1, 2, 3, 7], new(), allocator));
            using VideoFrame valid = decoder.Decode((byte[])[.. palette, 1, 2, 3, 8], new(), allocator);
            Assert.Equal(new byte[] { 3, 2, 1, 255 }, valid.Data.ToArray());
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(header, new(), allocator, new(true)));
        }

        private static byte[] Header(int width, int height, int depth, int descriptor, int type)
        {
            byte[] data = new byte[18]; data[2] = (byte)type;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)height);
            data[16] = (byte)depth; data[17] = (byte)descriptor;
            return data;
        }
    }
}
