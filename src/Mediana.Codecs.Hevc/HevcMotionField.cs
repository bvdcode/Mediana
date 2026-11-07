// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Stores current four-sample motion cells or immutable sixteen-sample temporal cells.</summary>
    internal class HevcMotionField : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int cellSize, columns;
        private bool disposed;

        public HevcMotionField(int width, int height, MediaBufferAllocator allocator) : this(width, height, 4, allocator) { }

        private HevcMotionField(int width, int height, int cellSize, MediaBufferAllocator allocator)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(allocator);
            Width = width; Height = height; this.cellSize = cellSize;
            columns = (width - 1) / cellSize + 1;
            int rows = (height - 1) / cellSize + 1;
            storage = allocator.Allocate(checked(columns * rows * Unsafe.SizeOf<HevcMotionSample>()));
        }

        public int Width { get; }
        public int Height { get; }

        public HevcMotionSample Get(int x, int y)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if ((uint)x >= Width || (uint)y >= Height) { return default; }
            return MemoryMarshal.Cast<byte, HevcMotionSample>(storage.Memory.Span)[y / cellSize * columns + x / cellSize];
        }

        public void Set(int x, int y, int width, int height, HevcMotionSample value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cellSize != 4) { throw new InvalidOperationException("Temporal motion fields are immutable."); }
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > Width - width || y > Height - height ||
                ((x | y | width | height) & 3) != 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
            value = value with { Flags = (byte)(value.Flags | HevcMotionSample.Available) };
            Span<HevcMotionSample> cells = MemoryMarshal.Cast<byte, HevcMotionSample>(storage.Memory.Span);
            for (int row = y / 4; row < (y + height) / 4; row++)
            { cells.Slice(row * columns + x / 4, width / 4).Fill(value); }
        }

        public HevcMotionField Compact(MediaBufferAllocator allocator)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cellSize != 4) { throw new InvalidOperationException("A temporal motion field is already compact."); }
            HevcMotionField result = new(Width, Height, 16, allocator);
            bool completed = false;
            try
            {
                Span<HevcMotionSample> cells = MemoryMarshal.Cast<byte, HevcMotionSample>(result.storage.Memory.Span);
                int index = 0;
                for (int y = 0; y < Height; y += 16)
                {
                    for (int x = 0; x < Width; x += 16) { cells[index++] = Get(x, y); }
                }
                completed = true; return result;
            }
            finally { if (!completed) { result.Dispose(); } }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            storage.Dispose(); disposed = true;
        }
    }
}
