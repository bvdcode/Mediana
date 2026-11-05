// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class Vp8Reconstruction : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int width;

        internal Vp8Reconstruction(int paddedWidth, MediaBufferAllocator allocator)
        {
            width = paddedWidth;
            storage = allocator.Allocate(checked(width * 24 + width * 3 / 4 + 18));
        }

        internal Span<byte> Plane(int plane) => plane switch
        {
            0 => storage.Memory.Span[..(width * 16)],
            1 => storage.Memory.Span.Slice(width * 16, width * 4),
            2 => storage.Memory.Span.Slice(width * 20, width * 4),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        internal Span<byte> Above(int plane) => plane switch
        {
            0 => storage.Memory.Span.Slice(width * 24, width / 4),
            1 => storage.Memory.Span.Slice(width * 24 + width / 4, width / 8),
            2 => storage.Memory.Span.Slice(width * 24 + width * 3 / 8, width / 8),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        internal Span<byte> Left => storage.Memory.Span.Slice(width * 24 + width / 2, 8);
        internal Span<byte> AboveModes => storage.Memory.Span.Slice(width * 24 + width / 2 + 8, width / 4);
        internal Span<byte> LeftModes => storage.Memory.Span.Slice(width * 24 + width * 3 / 4 + 8, 4);
        internal int Stride(int plane) => plane switch { 0 => width, 1 or 2 => width / 2, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };

        internal void BeginMacroblock(int x, bool firstRow)
        {
            Span<byte> corners = storage.Memory.Span[^6..];
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                int px = plane == 0 ? x : x / 2;
                corners[plane] = firstRow ? (byte)127 : px == 0 ? (byte)129 : corners[plane + 3];
                corners[plane + 3] = Plane(plane)[(size - 1) * Stride(plane) + px + size - 1];
            }
        }

        internal byte LumaNeighbors(int x, int y, bool firstRow, Span<byte> top, Span<byte> left)
        {
            ReadOnlySpan<byte> pixels = Plane(0);
            int previousRow = ((y + 15) & 15) * width;
            for (int index = 0; index < 4; index++)
            {
                top[index] = firstRow && y == 0 ? (byte)127 : pixels[previousRow + x + index];
                left[index] = x == 0 ? (byte)129 : pixels[(y + index) * width + x - 1];
            }
            bool rightEdge = (x & 15) == 12;
            for (int index = 4; index < 8; index++)
            {
                top[index] = firstRow && (y == 0 || rightEdge) ? (byte)127 :
                    pixels[(rightEdge ? 15 * width : previousRow) + Math.Min(x + index, width - 1)];
            }
            if (y == 0 && (x & 15) == 0) { return storage.Memory.Span[^6]; }
            if (firstRow && y == 0) { return 127; }
            if (x == 0) { return 129; }
            return pixels[previousRow + x - 1];
        }

        internal byte ChromaNeighbors(int plane, int x, bool firstRow, Span<byte> top, Span<byte> left)
        {
            ReadOnlySpan<byte> pixels = Plane(plane);
            int stride = width / 2;
            for (int index = 0; index < 8; index++)
            {
                top[index] = firstRow ? (byte)127 : pixels[7 * stride + x + index];
                left[index] = x == 0 ? (byte)129 : pixels[index * stride + x - 1];
            }
            return storage.Memory.Span[^6..][plane];
        }

        public void Dispose() => storage.Dispose();
    }
}
