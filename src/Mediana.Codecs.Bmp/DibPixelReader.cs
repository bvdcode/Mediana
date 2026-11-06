// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;

namespace Mediana.Codecs.Bmp
{
    internal static class DibPixelReader
    {
        internal static void Read(DibImage image, Span<byte> pixels, CancellationToken token)
        {
            if (image.Compression is 1 or 2) { ReadRle(image, pixels, token); return; }
            int stride = checked((int)(((long)image.Width * image.Depth + 31) / 32 * 4));
            image.Require(image.PixelOffset, checked(stride * image.Height));
            bool hasAlpha = !image.Icon;
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> row = image.Data.Span.Slice(image.PixelOffset + y * stride, stride);
                int targetY = image.TopDown ? y : image.Height - y - 1;
                for (int x = 0; x < image.Width; x++)
                {
                    uint value = image.Depth switch
                    {
                        1 => image.Palette((row[x / 8] >> (7 - x % 8)) & 1),
                        4 => image.Palette((row[x / 2] >> (4 - x % 2 * 4)) & 15),
                        8 => image.Palette(row[x]),
                        16 => Components(BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]), image),
                        24 => 0xff000000u | (uint)(row[x * 3 + 2] << 16 | row[x * 3 + 1] << 8 | row[x * 3]),
                        32 => Components(BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]), image),
                        _ => throw new NotSupportedException("Unsupported DIB pixel layout.")
                    };
                    if ((value >> 24) != 0) { hasAlpha = true; }
                    Write(pixels, targetY * image.Width + x, value);
                }
            }
            if (image.Icon && (image.Depth != 32 || !hasAlpha))
            {
                int maskStride = checked((int)(((long)image.Width + 31) / 32 * 4));
                int start = checked(image.PixelOffset + stride * image.Height);
                image.Require(start, checked(maskStride * image.Height));
                for (int y = 0; y < image.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < image.Width; x++)
                    {
                        byte mask = image.Data.Span[start + y * maskStride + x / 8];
                        pixels[((image.Height - y - 1) * image.Width + x) * 4 + 3] = (mask & (128 >> (x % 8))) == 0 ? (byte)255 : (byte)0;
                    }
                }
            }
        }

        private static uint Components(uint value, DibImage image) => (uint)(Channel(value, image.Red) << 16
            | Channel(value, image.Green) << 8 | Channel(value, image.Blue)) | (uint)(image.Alpha == 0 ? 255 : Channel(value, image.Alpha)) << 24;
        private static byte Channel(uint value, uint mask)
        {
            int shift = BitOperations.TrailingZeroCount(mask);
            uint maximum = mask >> shift;
            return (byte)((((ulong)(value & mask) >> shift) * 255 + maximum / 2) / maximum);
        }
        private static void Write(Span<byte> pixels, int index, uint color)
        {
            int offset = index * 4;
            pixels[offset] = (byte)(color >> 16); pixels[offset + 1] = (byte)(color >> 8);
            pixels[offset + 2] = (byte)color; pixels[offset + 3] = (byte)(color >> 24);
        }
        private static void ReadRle(DibImage image, Span<byte> pixels, CancellationToken token)
        {
            for (int i = 0; i < pixels.Length / 4; i++) { Write(pixels, i, image.Palette(0)); }
            int position = image.PixelOffset, x = 0, y = 0;
            while (position < image.Data.Length)
            {
                token.ThrowIfCancellationRequested(); image.Require(position, 2);
                int count = image.Data.Span[position++], value = image.Data.Span[position++];
                if (count != 0)
                {
                    for (int i = 0; i < count; i++) { Put(image, pixels, ref x, y, image.Compression == 1 ? value : (value >> (4 - i % 2 * 4)) & 15); }
                }
                else
                {
                    switch (value)
                    {
                        case 0: x = 0; y++; break;
                        case 1: return;
                        case 2:
                            image.Require(position, 2); x += image.Data.Span[position++]; y += image.Data.Span[position++];
                            if (x > image.Width || y >= image.Height) { throw new InvalidDataException("DIB RLE delta exceeds its canvas."); }
                            break;
                        default:
                            int bytes = image.Compression == 1 ? value : (value + 1) / 2;
                            image.Require(position, (bytes + 1) & ~1);
                            for (int i = 0; i < value; i++)
                            { Put(image, pixels, ref x, y, image.Compression == 1 ? image.Data.Span[position + i] : (image.Data.Span[position + i / 2] >> (4 - i % 2 * 4)) & 15); }
                            position += (bytes + 1) & ~1;
                            break;
                    }
                }
            }
            throw new InvalidDataException("DIB RLE is missing its end marker.");

        }
        private static void Put(DibImage image, Span<byte> pixels, ref int x, int y, int index)
        {
            if (x >= image.Width || y >= image.Height) { throw new InvalidDataException("DIB RLE run exceeds its canvas."); }
            Write(pixels, (image.Height - y - 1) * image.Width + x++, image.Palette(index));
        }
    }
}
