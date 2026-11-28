// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264LumaNeighborhood
    {
        private const int Block = 16;
        private const int Window = Block + 2;
        private const int Padding = 5;
        private const int Patch = Window + Padding;
        private const int Plane = Window * Window;
        internal const int SampleBytes = Patch * Patch;
        internal const int FilteredBytes = Plane * 3;
        private readonly ReadOnlySpan<byte> samples;
        private readonly ReadOnlySpan<byte> horizontal;
        private readonly ReadOnlySpan<byte> vertical;
        private readonly ReadOnlySpan<byte> diagonal;
        private readonly int sampleStride;
        private readonly int halfStride;
        private readonly int sampleOrigin;
        private readonly int halfOrigin;

        public H264LumaNeighborhood(H264Picture reference, long x, long y,
            Span<byte> samples, Span<byte> filtered)
        {
            if (samples.Length != SampleBytes || filtered.Length != FilteredBytes)
            {
                throw new ArgumentException("The luma neighborhood storage has unexpected dimensions.");
            }
            H264BorderExtension.Copy(reference.Plane(0), reference.Width, reference.Height,
                x - 3, y - 3, Patch, Patch, samples);
            Span<short> intermediate = stackalloc short[Patch * Window];
            for (int row = 0; row < Patch; row++)
            {
                int column = 0;
                for (; column <= Window - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    Vector128<short> sum = H264LumaFilter.Sum8(samples, row * Patch + column, 1);
                    sum.StoreUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(row * Window + column));
                }
                for (; column < Window; column++)
                {
                    intermediate[row * Window + column] = (short)H264LumaInterpolation.HorizontalSum(samples, row * Patch + column);
                }
            }
            for (int row = 0; row < Window; row++)
            {
                int column = 0;
                for (; column <= Window - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    int index = row * Window + column;
                    Vector128<short> sum = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)((row + 2) * Window + column));
                    H264VectorSamples.Write8(filtered, index, H264LumaFilter.Half8(sum));
                    Vector128<short> verticalSum = H264LumaFilter.Sum8(samples, row * Patch + column + 2, Patch);
                    H264VectorSamples.Write8(filtered, Plane + index, H264LumaFilter.Half8(verticalSum));
                    H264VectorSamples.Write8(filtered, 2 * Plane + index, H264LumaFilter.Diagonal8(intermediate, Window, index));
                }
                for (; column < Window; column++)
                {
                    int index = row * Window + column;
                    filtered[index] = (byte)H264LumaInterpolation.Half(intermediate[(row + 2) * Window + column]);
                    filtered[Plane + index] = (byte)H264LumaInterpolation.Vertical(samples, Patch, (row + 2) * Patch + column + 2);
                    filtered[2 * Plane + index] = (byte)H264LumaFilter.Diagonal(intermediate, Window, index);
                }
            }
            this.samples = samples;
            horizontal = filtered[..Plane];
            vertical = filtered.Slice(Plane, Plane);
            diagonal = filtered.Slice(2 * Plane, Plane);
            sampleStride = Patch;
            halfStride = Window;
            sampleOrigin = 3 * Patch + 3;
            halfOrigin = Window + 1;
        }

        internal H264LumaNeighborhood(H264MotionReference reference, int x, int y)
        {
            reference.PrepareNeighborhood(x, y);
            samples = reference.Samples;
            horizontal = reference.Horizontal;
            vertical = reference.Vertical;
            diagonal = reference.Diagonal;
            sampleStride = reference.SampleStride;
            halfStride = reference.HalfStride;
            sampleOrigin = (y + reference.Range + 2) * sampleStride + x + reference.Range + 2;
            halfOrigin = (y + reference.Range) * halfStride + x + reference.Range;
        }

        internal (int Left, int Right, int Above, int Below) DifferenceCross(ReadOnlySpan<byte> source,
            int x, int y, int step, int cutoff)
        {
            ReadOnlySpan<byte> left = HalfLattice(x - 2, y, out int horizontalStride);
            ReadOnlySpan<byte> above = HalfLattice(x, y - 2, out int verticalStride);
            int centerStride = 0;
            ReadOnlySpan<byte> center = step == 1 ? HalfLattice(x, y, out centerStride) : [];
            return H264FractionalCross.Difference(source, left, horizontalStride, above, verticalStride, center, centerStride, cutoff);
        }

        private ReadOnlySpan<byte> HalfLattice(int x, int y, out int stride)
        {
            int row = y >> 2;
            int column = x >> 2;
            if (((x | y) & 2) == 0)
            {
                stride = sampleStride;
                return samples[(sampleOrigin + row * sampleStride + column)..];
            }

            int start = halfOrigin + row * halfStride + column;
            stride = halfStride;
            return (x & 2, y & 2) switch
            {
                (2, 0) => horizontal[start..],
                (0, 2) => vertical[start..],
                (2, 2) => diagonal[start..],
                _ => throw new InvalidOperationException("The position has no fractional luma samples.")
            };
        }

        /// <summary>Scores the quarter-pixel neighbours of an integer or diagonal half-pixel center.</summary>
        internal (int UpperLeft, int Above, int UpperRight, int Left, int Right, int LowerLeft, int Below, int LowerRight)
            DifferenceEight(ReadOnlySpan<byte> source, int x, int y)
        {
            ReadOnlySpan<byte> left = HalfLattice(x - 2, y, out int horizontalStride);
            ReadOnlySpan<byte> above = HalfLattice(x, y - 2, out int verticalStride);
            ReadOnlySpan<byte> center = HalfLattice(x, y, out int centerStride);
            return H264FractionalEight.Difference(source, left, horizontalStride, above, verticalStride, center, centerStride);
        }

        public H264LumaCandidate Select(int x, int y)
        {
            if (x is < -3 or > 3 || y is < -3 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "The luma candidate must fit its prepared neighborhood.");
            }
            int left = x >> 2;
            int top = y >> 2;
            int integer = sampleOrigin + top * sampleStride + left;
            int half = halfOrigin + top * halfStride + left;
            return (x & 3, y & 3) switch
            {
                (0, 0) => new(samples[integer..], sampleStride),
                (1, 0) => new(samples[integer..], sampleStride, horizontal[half..], halfStride),
                (2, 0) => new(horizontal[half..], halfStride),
                (3, 0) => new(horizontal[half..], halfStride, samples[(integer + 1)..], sampleStride),
                (0, 1) => new(samples[integer..], sampleStride, vertical[half..], halfStride),
                (1, 1) => new(horizontal[half..], halfStride, vertical[half..], halfStride),
                (2, 1) => new(horizontal[half..], halfStride, diagonal[half..], halfStride),
                (3, 1) => new(horizontal[half..], halfStride, vertical[(half + 1)..], halfStride),
                (0, 2) => new(vertical[half..], halfStride),
                (1, 2) => new(vertical[half..], halfStride, diagonal[half..], halfStride),
                (2, 2) => new(diagonal[half..], halfStride),
                (3, 2) => new(diagonal[half..], halfStride, vertical[(half + 1)..], halfStride),
                (0, 3) => new(vertical[half..], halfStride, samples[(integer + sampleStride)..], sampleStride),
                (1, 3) => new(vertical[half..], halfStride, horizontal[(half + halfStride)..], halfStride),
                (2, 3) => new(diagonal[half..], halfStride, horizontal[(half + halfStride)..], halfStride),
                (3, 3) => new(vertical[(half + 1)..], halfStride, horizontal[(half + halfStride)..], halfStride),
                _ => throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.")
            };
        }
    }
}
