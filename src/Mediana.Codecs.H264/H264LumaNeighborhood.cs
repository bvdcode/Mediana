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
        }

        /// <summary>Stops after the first row exceeding the error limit. An error within the limit guarantees a complete output block.</summary>
        public int Evaluate(int x, int y, ReadOnlySpan<byte> comparison, Span<byte> output, int maximumError)
        {
            if (x is < -3 or > 3 || y is < -3 or > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "The luma candidate must fit its prepared neighborhood.");
            }
            if (comparison.Length != Block * Block || output.Length != Block * Block)
            {
                throw new ArgumentException("The luma comparison and prediction must be complete 16x16 blocks.");
            }
            int left = (x >> 2) + 1;
            int top = (y >> 2) + 1;
            int integer = (top + 2) * Patch + left + 2;
            int half = top * Window + left;
            switch (x & 3, y & 3)
            {
                case (0, 0): return Copy(samples, Patch, integer, comparison, output, maximumError);
                case (1, 0): return Average(samples, Patch, integer, horizontal, Window, half, comparison, output, maximumError);
                case (2, 0): return Copy(horizontal, Window, half, comparison, output, maximumError);
                case (3, 0): return Average(horizontal, Window, half, samples, Patch, integer + 1, comparison, output, maximumError);
                case (0, 1): return Average(samples, Patch, integer, vertical, Window, half, comparison, output, maximumError);
                case (1, 1): return Average(horizontal, Window, half, vertical, Window, half, comparison, output, maximumError);
                case (2, 1): return Average(horizontal, Window, half, diagonal, Window, half, comparison, output, maximumError);
                case (3, 1): return Average(horizontal, Window, half, vertical, Window, half + 1, comparison, output, maximumError);
                case (0, 2): return Copy(vertical, Window, half, comparison, output, maximumError);
                case (1, 2): return Average(vertical, Window, half, diagonal, Window, half, comparison, output, maximumError);
                case (2, 2): return Copy(diagonal, Window, half, comparison, output, maximumError);
                case (3, 2): return Average(diagonal, Window, half, vertical, Window, half + 1, comparison, output, maximumError);
                case (0, 3): return Average(vertical, Window, half, samples, Patch, integer + Patch, comparison, output, maximumError);
                case (1, 3): return Average(vertical, Window, half, horizontal, Window, half + Window, comparison, output, maximumError);
                case (2, 3): return Average(diagonal, Window, half, horizontal, Window, half + Window, comparison, output, maximumError);
                case (3, 3): return Average(vertical, Window, half + 1, horizontal, Window, half + Window, comparison, output, maximumError);
                default: throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.");
            }
        }

        private static int Copy(ReadOnlySpan<byte> source, int stride, int start, ReadOnlySpan<byte> comparison, Span<byte> output, int maximumError)
        {
            int error = 0;
            for (int row = 0; row < Block; row++)
            {
                Vector128<byte> prediction = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(start + row * stride));
                Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(comparison), (nuint)(row * Block));
                error += H264SampleDifference.Sum16(original, prediction);
                prediction.StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)(row * Block));
                if (error > maximumError)
                {
                    break;
                }
            }
            return error;
        }

        private static int Average(ReadOnlySpan<byte> first, int firstStride, int firstStart,
            ReadOnlySpan<byte> second, int secondStride, int secondStart, ReadOnlySpan<byte> comparison, Span<byte> output, int maximumError)
        {
            int error = 0;
            for (int row = 0; row < Block; row++)
            {
                Vector128<byte> a = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(first), (nuint)(firstStart + row * firstStride));
                Vector128<byte> b = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(second), (nuint)(secondStart + row * secondStride));
                Vector128<byte> rounded = H264SampleAverage.Rounded(a, b);
                Vector128<byte> original = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(comparison), (nuint)(row * Block));
                error += H264SampleDifference.Sum16(original, rounded);
                rounded.StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)(row * Block));
                if (error > maximumError)
                {
                    break;
                }
            }
            return error;
        }
    }
}
