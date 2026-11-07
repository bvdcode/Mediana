// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Hevc
{
    /// <summary>
    /// Rents a component in its coded sample width and a four-sample availability map.
    /// </summary>
    /// <remarks>
    /// Rentals are exclusive to one picture, cleared before use and returned after rendering.
    /// Eight-bit components use bytes; ten-bit components use unsigned words. Views borrow
    /// storage only within the picture lifetime. Availability follows complete TU writes.
    /// </remarks>
    internal class SamplePlane : IDisposable
    {
        private readonly byte[] bytes = Array.Empty<byte>();
        private readonly ushort[] words = Array.Empty<ushort>();
        private readonly bool[] available = Array.Empty<bool>();
        private readonly Memory<byte> borrowed;
        private bool frozen, availabilityReturned, disposed;

        public SamplePlane(int width, int height, int bitDepth) : this(width, height, bitDepth, Memory<byte>.Empty, true) { }

        /// <summary>Borrows pixel storage from the containing picture; this plane owns only its availability map.</summary>
        public SamplePlane(int width, int height, int bitDepth, Memory<byte> pixels) : this(width, height, bitDepth, pixels, false) { }

        private SamplePlane(int width, int height, int bitDepth, Memory<byte> pixels, bool rentPixels)
        {
            if ((width <= 0) || (height <= 0) || ((width % 4) != 0) || ((height % 4) != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if ((bitDepth != 8) && (bitDepth != 10))
            {
                throw new ArgumentOutOfRangeException(nameof(bitDepth));
            }
            Width = width;
            Height = height;
            BitDepth = bitDepth;
            int count = checked(width * height);
            borrowed = rentPixels ? Memory<byte>.Empty : pixels[..checked(count * ((bitDepth + 7) / 8))];
            bool initialized = false;
            try
            {
                if (rentPixels)
                {
                    switch (bitDepth)
                    {
                        case 8:
                            bytes = ArrayPool<byte>.Shared.Rent(count);
                            bytes.AsSpan(0, count).Clear();
                            break;
                        case 10:
                            words = ArrayPool<ushort>.Shared.Rent(count);
                            words.AsSpan(0, count).Clear();
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported component sample depth.");
                    }
                }
                available = ArrayPool<bool>.Shared.Rent(count / 16);
                available.AsSpan(0, count / 16).Clear();
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

        public int Width { get; }
        public int Height { get; }
        public int BitDepth { get; }
        public int Stride => Width;

        public SamplePlaneView<byte> ByteView
        {
            get
            {
                ThrowIfDisposed();
                if (BitDepth != 8)
                {
                    throw new InvalidOperationException("The component does not use byte samples.");
                }
                ThrowIfFrozen();
                return new SamplePlaneView<byte>(borrowed.IsEmpty ? bytes.AsSpan() : borrowed.Span, Width, Height);
            }
        }

        public SamplePlaneView<ushort> WordView
        {
            get
            {
                ThrowIfDisposed();
                if (BitDepth != 10)
                {
                    throw new InvalidOperationException("The component does not use word samples.");
                }
                ThrowIfFrozen();
                return new SamplePlaneView<ushort>(borrowed.IsEmpty ? words.AsSpan() : MemoryMarshal.Cast<byte, ushort>(borrowed.Span), Width, Height);
            }
        }

        public ReadOnlySamplePlaneView<byte> ReadByteView
        {
            get
            {
                ThrowIfDisposed();
                if (BitDepth != 8) { throw new InvalidOperationException("The component does not use byte samples."); }
                return new(borrowed.IsEmpty ? bytes.AsSpan() : borrowed.Span, Width, Height);
            }
        }

        public ReadOnlySamplePlaneView<ushort> ReadWordView
        {
            get
            {
                ThrowIfDisposed();
                if (BitDepth != 10) { throw new InvalidOperationException("The component does not use word samples."); }
                return new(borrowed.IsEmpty ? words.AsSpan() : MemoryMarshal.Cast<byte, ushort>(borrowed.Span), Width, Height);
            }
        }

        public void Freeze()
        {
            ThrowIfDisposed();
            if (frozen) { return; }
            ArrayPool<bool>.Shared.Return(available);
            availabilityReturned = true;
            frozen = true;
        }

        public int Neighbour(int x, int y)
        {
            ThrowIfDisposed();
            ThrowIfFrozen();
            if ((x < 0) || (y < 0) || (x >= Width) || (y >= Height) || !available[((y / 4) * (Width / 4)) + (x / 4)])
            {
                return -1;
            }
            int index = (y * Stride) + x;
            if (!borrowed.IsEmpty)
            {
                return BitDepth switch
                {
                    8 => borrowed.Span[index],
                    10 => MemoryMarshal.Cast<byte, ushort>(borrowed.Span)[index],
                    _ => throw new InvalidOperationException("Unsupported component sample depth.")
                };
            }
            return BitDepth switch
            {
                8 => bytes[index],
                10 => words[index],
                _ => throw new InvalidOperationException("Unsupported component sample depth.")
            };
        }

        public void Store(int x, int y, int size, ReadOnlySpan<int> prediction, ReadOnlySpan<int> residual)
        {
            ThrowIfDisposed();
            switch (BitDepth)
            {
                case 8:
                    Store<byte, ByteSampleFormat>(ByteView, x, y, size, prediction, residual);
                    break;
                case 10:
                    Store<ushort, WordSampleFormat>(WordView, x, y, size, prediction, residual);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported component sample depth.");
            }
            for (int row = y / 4; row < ((y + size) / 4); row++)
            {
                available.AsSpan((row * (Width / 4)) + (x / 4), size / 4).Fill(true);
            }
        }

        private void Store<TSample, TFormat>(SamplePlaneView<TSample> plane, int x, int y, int size,
            ReadOnlySpan<int> prediction, ReadOnlySpan<int> residual)
            where TSample : unmanaged
            where TFormat : struct, ISampleFormat<TSample>
        {
            int maximum = (1 << BitDepth) - 1;
            for (int row = 0; row < size; row++)
            {
                Span<TSample> destination = plane.Row(y + row).Slice(x, size);
                for (int column = 0; column < size; column++)
                {
                    int index = (row * size) + column;
                    int sample = Math.Max(0, Math.Min(maximum, prediction[index] + residual[index]));
                    destination[column] = default(TFormat).Write(sample);
                }
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                if (bytes.Length != 0)
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
                if (words.Length != 0)
                {
                    ArrayPool<ushort>.Shared.Return(words);
                }
                if (available.Length != 0 && !availabilityReturned)
                {
                    ArrayPool<bool>.Shared.Return(available);
                }
            }
        }

        private void ThrowIfFrozen()
        {
            if (frozen) { throw new InvalidOperationException("Published HEVC samples cannot be modified or used for intra availability."); }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SamplePlane), "Sample storage has been returned to its pool.");
            }
        }
    }
}
