// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Stores slice-local coding metadata in four-luma-sample cells without picture padding.
    /// </summary>
    /// <remarks>
    /// Rentals belong to one picture and remain live until all reconstruction workers
    /// and in-loop filters finish. Every logical cell is initialized before reuse.
    /// </remarks>
    internal class CodingBlockMap : IDisposable
    {
        private const sbyte UnavailableQp = sbyte.MinValue;
        private readonly ArrayPool<byte> metadataPool;
        private readonly ArrayPool<sbyte> quantizerPool;
        private readonly ArrayPool<bool> bypassPool;
        private readonly byte[] modes = Array.Empty<byte>();
        private readonly byte[] depths = Array.Empty<byte>();
        private readonly sbyte[] quantizers = Array.Empty<sbyte>();
        private readonly byte[] boundaries = Array.Empty<byte>();
        private readonly bool[] bypass = Array.Empty<bool>();
        private bool disposed;

        public CodingBlockMap(int width, int height, PicturePartitions? partitions = null)
            : this(width, height, ArrayPool<byte>.Shared, ArrayPool<sbyte>.Shared, ArrayPool<bool>.Shared)
        {
            Partitions = partitions;
        }

        internal CodingBlockMap(int width, int height, ArrayPool<byte> metadataPool,
            ArrayPool<sbyte> quantizerPool, ArrayPool<bool> bypassPool)
        {
            this.metadataPool = metadataPool ?? throw new ArgumentNullException(nameof(metadataPool));
            this.quantizerPool = quantizerPool ?? throw new ArgumentNullException(nameof(quantizerPool));
            this.bypassPool = bypassPool ?? throw new ArgumentNullException(nameof(bypassPool));
            Width = width / 4;
            Height = height / 4;
            int count = checked(Width * Height);
            bool initialized = false;
            try
            {
                modes = metadataPool.Rent(count);
                depths = metadataPool.Rent(count);
                quantizers = quantizerPool.Rent(count);
                boundaries = metadataPool.Rent(count);
                bypass = bypassPool.Rent(count);
                modes.AsSpan(0, count).Fill(byte.MaxValue);
                depths.AsSpan(0, count).Fill(byte.MaxValue);
                quantizers.AsSpan(0, count).Fill(UnavailableQp);
                boundaries.AsSpan(0, count).Clear();
                bypass.AsSpan(0, count).Clear();
                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    Dispose();
                }
            }
        }

        public PicturePartitions? Partitions { get; }
        public int Width { get; }
        public int Height { get; }
        public int Mode(int x, int y)
        {
            ThrowIfDisposed();
            return Inside(x, y) && (modes[Index(x, y)] != byte.MaxValue) ? modes[Index(x, y)] : 1;
        }

        public int Depth(int x, int y)
        {
            ThrowIfDisposed();
            return Inside(x, y) && (depths[Index(x, y)] != byte.MaxValue) ? depths[Index(x, y)] : -1;
        }

        public int Qp(int x, int y)
        {
            ThrowIfDisposed();
            if (!Inside(x, y))
            {
                return int.MinValue;
            }

            int qp = quantizers[Index(x, y)];
            return qp == UnavailableQp ? int.MinValue : qp;
        }
        public bool Bypass(int x, int y)
        {
            ThrowIfDisposed();
            return bypass[Index(x, y)];
        }

        public bool Boundary(int x, int y, bool vertical)
        {
            ThrowIfDisposed();
            return (boundaries[Index(x, y)] & (vertical ? 1 : 2)) != 0;
        }

        public void SetCodingUnit(int x, int y, int size, int depth, bool transquantBypass)
        {
            ThrowIfDisposed();
            for (int row = y / 4; row < ((y + size) / 4); row++)
            {
                depths.AsSpan((row * Width) + (x / 4), size / 4).Fill((byte)depth);
                bypass.AsSpan((row * Width) + (x / 4), size / 4).Fill(transquantBypass);
            }

            MarkTransform(x, y, size);
        }

        public void SetMode(int x, int y, int size, int mode)
        {
            ThrowIfDisposed();
            for (int row = y / 4; row < ((y + size) / 4); row++)
            {
                modes.AsSpan((row * Width) + (x / 4), size / 4).Fill((byte)mode);
            }
        }

        public void SetQp(int x, int y, int width, int height, int qp)
        {
            ThrowIfDisposed();
            if ((qp <= UnavailableQp) || (qp > sbyte.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(qp));
            }

            for (int row = y / 4; row < ((y + height) / 4); row++)
            {
                quantizers.AsSpan((row * Width) + (x / 4), width / 4).Fill((sbyte)qp);
            }
        }

        public void MarkTransform(int x, int y, int size)
        {
            ThrowIfDisposed();
            for (int i = 0; i < size; i += 4)
            {
                boundaries[Index(x, y + i)] |= 1;
                boundaries[Index(x + i, y)] |= 2;
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                Return(metadataPool, modes);
                Return(metadataPool, depths);
                Return(quantizerPool, quantizers);
                Return(metadataPool, boundaries);
                Return(bypassPool, bypass);
            }
        }

        private static void Return<T>(ArrayPool<T> pool, T[] buffer)
        {
            if (buffer.Length > 0)
            {
                pool.Return(buffer);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CodingBlockMap), "Coding metadata has been returned to its pools.");
            }
        }

        private bool Inside(int x, int y) => (x >= 0) && (y >= 0) && (x < (Width * 4)) && (y < (Height * 4));
        private int Index(int x, int y) => ((y / 4) * Width) + (x / 4);
    }
}
