// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal static class WebpColorIndex
    {
        private const int TableBits = 10;
        private const int TableSize = 1 << TableBits;

        internal static WebpEntropyStream? TryTransform(Span<uint> pixels, int width, int height, MediaBufferAllocator allocator,
            CancellationToken token, out int count, out int packedWidth)
        {
            Span<uint> keys = stackalloc uint[TableSize];
            Span<int> indices = stackalloc int[TableSize];
            Span<uint> colors = stackalloc uint[256];
            indices.Clear();
            int colorCount = 0;
            count = 0;
            packedWidth = width;
            for (int index = 0; index < pixels.Length; index++)
            {
                if ((index & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                uint pixel = pixels[index];
                int slot = Find(keys, indices, pixel);
                if (indices[slot] == 0)
                {
                    if (colorCount == 256) { return null; }
                    keys[slot] = pixel;
                    colors[colorCount++] = pixel;
                    indices[slot] = colorCount;
                }
            }
            if (pixels.Length < colorCount * 4) { return null; }
            int widthBits = colorCount switch
            {
                <= 2 => 3,
                <= 4 => 2,
                <= 16 => 1,
                _ => 0
            };
            int packedPixels = 1 << widthBits;
            int indexBits = 8 >> widthBits;
            packedWidth = (width + packedPixels - 1) >> widthBits;
            for (int row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                for (int column = 0; column < packedWidth; column++)
                {
                    uint packed = 0;
                    for (int part = 0; part < packedPixels && (column * packedPixels + part) < width; part++)
                    {
                        uint pixel = pixels[row * width + column * packedPixels + part];
                        packed |= (uint)(indices[Find(keys, indices, pixel)] - 1) << (part * indexBits);
                    }
                    pixels[row * packedWidth + column] = 0xff000000u | (packed << 8);
                }
            }
            count = colorCount;
            Span<uint> deltas = stackalloc uint[256];
            uint previous = 0;
            for (int index = 0; index < colorCount; index++)
            {
                uint value = colors[index];
                deltas[index] = WebpPredictor.Subtract(value, previous);
                previous = value;
            }
            return WebpEntropyStream.Create(deltas[..colorCount], colorCount, allocator, token);
        }

        private static int Find(ReadOnlySpan<uint> keys, ReadOnlySpan<int> indices, uint pixel)
        {
            int slot = (int)(unchecked(pixel * 0x1e35a7bd) >> (32 - TableBits));
            while (indices[slot] != 0 && keys[slot] != pixel) { slot = (slot + 1) & (TableSize - 1); }
            return slot;
        }
    }
}
