// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264MotionReference : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int sampleBytes;
        private readonly int planeBytes;
        private readonly int rows;
        private readonly int pictureWidth;
        private readonly int pictureHeight;

        public H264MotionReference(H264Picture picture, int range, MediaBufferAllocator allocator)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range);
            Range = range;
            pictureWidth = picture.Width;
            pictureHeight = picture.Height;
            HalfStride = checked(picture.Width + range * 2 + 1);
            rows = checked(picture.Height + range * 2 + 1);
            SampleStride = checked(HalfStride + 5);
            sampleBytes = checked(SampleStride * (rows + 5));
            planeBytes = checked(HalfStride * rows);
            storage = allocator.Allocate(checked(sampleBytes + planeBytes * 3 + HalfStride * 12 * sizeof(short)));
        }

        public int Range { get; }
        public int SampleStride { get; }
        public int HalfStride { get; }
        public ReadOnlySpan<byte> Samples => storage.Span[..sampleBytes];
        public ReadOnlySpan<byte> Horizontal => storage.Span.Slice(sampleBytes, planeBytes);
        public ReadOnlySpan<byte> Vertical => storage.Span.Slice(sampleBytes + planeBytes, planeBytes);
        public ReadOnlySpan<byte> Diagonal => storage.Span.Slice(sampleBytes + planeBytes * 2, planeBytes);

        public void Prepare(H264Picture picture, CancellationToken token)
        {
            if (picture.Width != pictureWidth || picture.Height != pictureHeight)
            {
                throw new ArgumentException("The motion reference dimensions changed.", nameof(picture));
            }
            token.ThrowIfCancellationRequested();
            Span<byte> samples = storage.Span[..sampleBytes];
            H264BorderExtension.Copy(picture.ReadPlane(0), picture.Width, picture.Height,
                -Range - 2, -Range - 2, SampleStride, rows + 5, samples);
            Span<short> intermediate = MemoryMarshal.Cast<byte, short>(storage.Span[(sampleBytes + planeBytes * 3)..]);
            Span<byte> horizontal = storage.Span.Slice(sampleBytes, planeBytes);
            Span<byte> vertical = storage.Span.Slice(sampleBytes + planeBytes, planeBytes);
            Span<byte> diagonal = storage.Span.Slice(sampleBytes + planeBytes * 2, planeBytes);
            for (int row = 0; row < rows + 5; row++)
            {
                token.ThrowIfCancellationRequested();
                int start = row % 6 * HalfStride;
                int column = 0;
                for (; column <= HalfStride - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    H264LumaFilter.Sum8(samples, row * SampleStride + column, 1)
                        .StoreUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(start + column));
                }
                for (; column < HalfStride; column++)
                {
                    intermediate[start + column] = (short)H264LumaFilter.Sum(samples, row * SampleStride + column, 1);
                }
                intermediate.Slice(start, HalfStride).CopyTo(intermediate[(start + 6 * HalfStride)..]);
                if (row >= 5)
                {
                    FilterRow(samples, intermediate[((row - 5) % 6 * HalfStride)..], horizontal, vertical, diagonal, row - 5);
                }
            }
        }

        private void FilterRow(ReadOnlySpan<byte> samples, ReadOnlySpan<short> intermediate,
            Span<byte> horizontal, Span<byte> vertical, Span<byte> diagonal, int row)
        {
            int column = 0;
            for (; column <= HalfStride - Vector128<short>.Count; column += Vector128<short>.Count)
            {
                int offset = row * HalfStride + column;
                Vector128<short> sum = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(2 * HalfStride + column));
                H264VectorSamples.Write8(horizontal, offset, H264LumaFilter.Half8(sum));
                H264VectorSamples.Write8(vertical, offset,
                    H264LumaFilter.Half8(H264LumaFilter.Sum8(samples, row * SampleStride + column + 2, SampleStride)));
                H264VectorSamples.Write8(diagonal, offset, H264LumaFilter.Diagonal8(intermediate, HalfStride, column));
            }
            for (; column < HalfStride; column++)
            {
                int offset = row * HalfStride + column;
                horizontal[offset] = (byte)H264LumaInterpolation.Half(intermediate[2 * HalfStride + column]);
                vertical[offset] = (byte)H264LumaInterpolation.Half(H264LumaFilter.Sum(samples, row * SampleStride + column + 2, SampleStride));
                diagonal[offset] = (byte)H264LumaFilter.Diagonal(intermediate, HalfStride, column);
            }
        }

        public void Dispose() => storage.Dispose();
    }
}
