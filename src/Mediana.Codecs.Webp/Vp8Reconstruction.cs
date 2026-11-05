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
            storage = allocator.Allocate(checked(width * 24 + width / 2 + 8));
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
        internal int Stride(int plane) => plane switch { 0 => width, 1 or 2 => width / 2, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };

        internal byte LumaDc(int x, int y, bool firstRow)
        {
            ReadOnlySpan<byte> pixels = Plane(0);
            int top = 0;
            int left = 0;
            for (int index = 0; index < 4; index++)
            {
                top += firstRow && y == 0 ? 127 : pixels[((y + 15) & 15) * width + x + index];
                left += x == 0 ? 129 : pixels[(y + index) * width + x - 1];
            }
            return (byte)((top + left + 4) >> 3);
        }

        internal byte ChromaDc(int plane, int x, bool firstRow)
        {
            ReadOnlySpan<byte> pixels = Plane(plane);
            int stride = width / 2;
            int sum = 0;
            int count = 0;
            if (!firstRow)
            {
                for (int index = 0; index < 8; index++) { sum += pixels[7 * stride + x + index]; }
                count += 8;
            }
            if (x != 0)
            {
                for (int index = 0; index < 8; index++) { sum += pixels[index * stride + x - 1]; }
                count += 8;
            }
            return count == 0 ? (byte)128 : (byte)((sum + count / 2) / count);
        }

        public void Dispose() => storage.Dispose();
    }
}
