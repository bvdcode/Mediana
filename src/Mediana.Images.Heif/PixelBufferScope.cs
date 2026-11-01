// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;
using System.Collections.Generic;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Owns temporary pixel rentals or allocates storage with a caller-owned lifetime.
    /// </summary>
    /// <remarks>
    /// Temporary images are consumed before this scope is disposed. Every logical byte
    /// must be written before an image is read; rented capacity outside that range is hidden.
    /// Each scope belongs to one image operation and is not shared by workers.
    /// </remarks>
    internal class PixelBufferScope(bool temporary) : IDisposable
    {
        private readonly List<byte[]> rentals = new List<byte[]>();
        private bool disposed;

        public Memory<byte> Allocate(int length, Memory<byte> destination = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PixelBufferScope), "Pixel buffers cannot be allocated after their scope is disposed.");
            }
            if (!destination.IsEmpty)
            {
                if (destination.Length != length)
                {
                    throw new ArgumentException("The pixel destination must match the complete output size.", nameof(destination));
                }
                return destination;
            }
            if (!temporary)
            {
                return new byte[length];
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            bool retained = false;
            try
            {
                rentals.Add(buffer);
                retained = true;
                return buffer.AsMemory(0, length);
            }
            finally
            {
                if (!retained)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public void Release(ReadOnlyMemory<byte> pixels)
        {
            for (int index = 0; index < rentals.Count; index++)
            {
                byte[] buffer = rentals[index];
                if (buffer.Length >= pixels.Length && pixels.Equals((ReadOnlyMemory<byte>)buffer.AsMemory(0, pixels.Length)))
                {
                    rentals.RemoveAt(index);
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                foreach (byte[] buffer in rentals)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                rentals.Clear();
            }
        }
    }
}
