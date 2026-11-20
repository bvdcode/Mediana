// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Tga
{
    internal static class TgaPixelReader
    {
        public static void Decode(TgaImage image, Span<byte> destination, CancellationToken token)
        {
            if (image.AlphaType == 2) { throw new NotSupportedException("TGA alpha data has an undefined interpretation."); }
            ReadOnlySpan<byte> data = image.Raster.Span;
            int offset = 0, written = 0, pixels = checked(image.Width * image.Height);
            Span<byte> pixel = stackalloc byte[4];
            while (written < pixels)
            {
                token.ThrowIfCancellationRequested();
                int count = 1;
                bool repeat = false;
                if (image.Type >= 9)
                {
                    if (offset == data.Length) { throw new InvalidDataException("Truncated TGA RLE packet."); }
                    byte packet = data[offset++];
                    count = (packet & 127) + 1; repeat = (packet & 128) != 0;
                }
                if (count > pixels - written) { throw new InvalidDataException("TGA RLE packet exceeds the raster."); }
                for (int i = 0; i < count; i++)
                {
                    if (i == 0 || !repeat) { ReadPixel(image, data, ref offset, pixel); }
                    int x = written % image.Width, y = Row(written / image.Width, image.Height, image.Descriptor >> 6);
                    if ((image.Descriptor & 16) != 0) { x = image.Width - 1 - x; }
                    if ((image.Descriptor & 32) == 0) { y = image.Height - 1 - y; }
                    pixel.CopyTo(destination.Slice((y * image.Width + x) * 4, 4));
                    written++;
                }
            }
            if (offset != data.Length) { throw new InvalidDataException("Unexpected data after the TGA raster."); }
        }

        private static int Row(int row, int height, int interleave)
        {
            int step = interleave switch { 0 => 1, 1 => 2, 2 => 4, _ => throw new InvalidDataException("Invalid TGA interleave mode.") };
            for (int pass = 0; pass < step; pass++)
            {
                int count = (height + step - 1 - pass) / step;
                if (row < count) { return row * step + pass; }
                row -= count;
            }
            throw new InvalidDataException("Invalid TGA scan line.");
        }

        private static void ReadPixel(TgaImage image, ReadOnlySpan<byte> data, ref int offset, Span<byte> pixel)
        {
            int bytes = (image.Depth + 7) / 8;
            if (bytes > data.Length - offset) { throw new InvalidDataException("Truncated TGA pixel."); }
            ReadOnlySpan<byte> value = data.Slice(offset, bytes);
            offset += bytes;
            int depth = image.Depth;
            bool gray = image.Type is 3 or 11;
            if (image.Type is 1 or 9)
            {
                int index = depth == 8 ? value[0] : BinaryPrimitives.ReadUInt16LittleEndian(value);
                index -= image.PaletteStart;
                if ((uint)index >= image.PaletteCount) { throw new InvalidDataException("TGA index exceeds its palette."); }
                depth = image.PaletteDepth;
                bytes = (depth + 7) / 8;
                value = image.Palette.Span.Slice(index * bytes, bytes);
            }
            pixel[3] = 255;
            if (gray)
            {
                pixel[0] = value[0]; pixel[1] = value[0]; pixel[2] = value[0];
                if (depth == 16) { pixel[3] = value[1]; }
            }
            else if (depth is 15 or 16)
            {
                int packed = BinaryPrimitives.ReadUInt16LittleEndian(value);
                pixel[0] = Expand((packed >> 10) & 31); pixel[1] = Expand((packed >> 5) & 31); pixel[2] = Expand(packed & 31);
                if (depth == 16 && (image.Descriptor & 15) != 0) { pixel[3] = (byte)((packed >> 15) * 255); }
            }
            else
            {
                pixel[0] = value[2]; pixel[1] = value[1]; pixel[2] = value[0];
                if (depth == 32) { pixel[3] = value[3]; }
            }
            if (image.AlphaType is 0 or 1) { pixel[3] = 255; }
            else if (image.AlphaType == 4)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (pixel[c] > pixel[3]) { throw new InvalidDataException("TGA associated RGB exceeds alpha."); }
                    pixel[c] = pixel[3] == 0 ? (byte)0 : (byte)Math.Min(255, (pixel[c] * 255 + pixel[3] / 2) / pixel[3]);
                }
            }
        }
        private static byte Expand(int value) => (byte)((value << 3) | (value >> 2));
    }
}
