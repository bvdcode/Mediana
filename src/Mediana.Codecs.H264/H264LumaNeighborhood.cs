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
            Span<int> intermediate = stackalloc int[Patch * Window];
            for (int row = 0; row < Patch; row++)
            {
                int column = 0;
                for (; column <= Window - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    Vector128<short> sum = H264LumaFilter.Sum8(samples, row * Patch + column, 1);
                    Vector128.WidenLower(sum).StoreUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(row * Window + column));
                    Vector128.WidenUpper(sum).StoreUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)(row * Window + column + Vector128<int>.Count));
                }
                for (; column < Window; column++)
                {
                    intermediate[row * Window + column] = H264LumaInterpolation.HorizontalSum(samples, row * Patch + column);
                }
            }
            for (int row = 0; row < Window; row++)
            {
                int column = 0;
                for (; column <= Window - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    int index = row * Window + column;
                    Vector128<int> lower = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)((row + 2) * Window + column));
                    Vector128<int> upper = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(intermediate), (nuint)((row + 2) * Window + column + Vector128<int>.Count));
                    H264LumaFilter.Half8(Vector128.Narrow(lower, upper)).StoreUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)index);
                    Vector128<short> verticalSum = H264LumaFilter.Sum8(samples, row * Patch + column + 2, Patch);
                    H264LumaFilter.Half8(verticalSum).StoreUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)(Plane + index));
                    H264LumaFilter.Diagonal8(intermediate, Window, index).StoreUnsafe(ref MemoryMarshal.GetReference(filtered), (nuint)(2 * Plane + index));
                }
                for (; column < Window; column++)
                {
                    int index = row * Window + column;
                    filtered[index] = (byte)H264LumaInterpolation.Half(intermediate[(row + 2) * Window + column]);
                    filtered[Plane + index] = (byte)H264LumaInterpolation.Vertical(samples, Patch, (row + 2) * Patch + column + 2);
                    filtered[2 * Plane + index] = (byte)H264LumaInterpolation.Diagonal(intermediate, Window, index);
                }
            }
            this.samples = samples;
            horizontal = filtered[..Plane];
            vertical = filtered.Slice(Plane, Plane);
            diagonal = filtered.Slice(2 * Plane, Plane);
        }

        public void Read(int x, int y, Span<byte> output)
        {
            if (x is < -3 or > 3 || y is < -3 or > 3 || output.Length != Block * Block)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "The luma candidate must fit its prepared neighborhood.");
            }
            int left = (x >> 2) + 1;
            int top = (y >> 2) + 1;
            int integer = (top + 2) * Patch + left + 2;
            int half = top * Window + left;
            switch (x & 3, y & 3)
            {
                case (0, 0): Copy(samples, Patch, integer, output); break;
                case (1, 0): Average(samples, Patch, integer, horizontal, Window, half, output); break;
                case (2, 0): Copy(horizontal, Window, half, output); break;
                case (3, 0): Average(horizontal, Window, half, samples, Patch, integer + 1, output); break;
                case (0, 1): Average(samples, Patch, integer, vertical, Window, half, output); break;
                case (1, 1): Average(horizontal, Window, half, vertical, Window, half, output); break;
                case (2, 1): Average(horizontal, Window, half, diagonal, Window, half, output); break;
                case (3, 1): Average(horizontal, Window, half, vertical, Window, half + 1, output); break;
                case (0, 2): Copy(vertical, Window, half, output); break;
                case (1, 2): Average(vertical, Window, half, diagonal, Window, half, output); break;
                case (2, 2): Copy(diagonal, Window, half, output); break;
                case (3, 2): Average(diagonal, Window, half, vertical, Window, half + 1, output); break;
                case (0, 3): Average(vertical, Window, half, samples, Patch, integer + Patch, output); break;
                case (1, 3): Average(vertical, Window, half, horizontal, Window, half + Window, output); break;
                case (2, 3): Average(diagonal, Window, half, horizontal, Window, half + Window, output); break;
                case (3, 3): Average(vertical, Window, half + 1, horizontal, Window, half + Window, output); break;
                default: throw new InvalidOperationException("An H.264 luma fraction is outside its two-bit range.");
            }
        }

        private static void Copy(ReadOnlySpan<byte> source, int stride, int start, Span<byte> output)
        {
            for (int row = 0; row < Block; row++)
            {
                source.Slice(start + row * stride, Block).CopyTo(output.Slice(row * Block, Block));
            }
        }

        private static void Average(ReadOnlySpan<byte> first, int firstStride, int firstStart,
            ReadOnlySpan<byte> second, int secondStride, int secondStart, Span<byte> output)
        {
            Vector128<byte> even = Vector128.Create((byte)254);
            for (int row = 0; row < Block; row++)
            {
                Vector128<byte> a = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(first), (nuint)(firstStart + row * firstStride));
                Vector128<byte> b = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(second), (nuint)(secondStart + row * secondStride));
                Vector128<byte> difference = Vector128.BitwiseAnd(Vector128.Xor(a, b), even);
                Vector128<byte> rounded = Vector128.Subtract(Vector128.BitwiseOr(a, b), Vector128.ShiftRightLogical(difference.AsUInt16(), 1).AsByte());
                rounded.StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)(row * Block));
            }
        }
    }
}
