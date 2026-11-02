// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264ForwardTransform
    {
        private static ReadOnlySpan<int> Scan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<byte> ScanScales => [0, 1, 1, 0, 2, 0, 1, 1, 1, 1, 2, 0, 2, 1, 1, 2];
        private static ReadOnlySpan<int> Even => [13107, 11916, 10082, 9362, 8192, 7282];
        private static ReadOnlySpan<int> Mixed => [8066, 7490, 6554, 5825, 5243, 4559];
        private static ReadOnlySpan<int> Odd => [5243, 4660, 4194, 3647, 3355, 2893];

        public static void Transform4x4(ReadOnlySpan<int> residual, Span<int> transformed)
        {
            if (residual.Length != 16 || transformed.Length != 16)
            {
                throw new ArgumentException("The forward H.264 transform requires 16 samples.");
            }
            if (Sse2.IsSupported)
            {
                TransformSse2(residual, transformed);
                return;
            }
            residual.CopyTo(transformed);
            for (int row = 0; row < 4; row++)
            {
                TransformLine(transformed, row * 4, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                TransformLine(transformed, column, 4);
            }
        }

        public static void Quantize4x4(ReadOnlySpan<int> transformed, int qp, Span<int> scanned)
        {
            if (transformed.Length != 16 || scanned.Length is not (15 or 16) || qp is < 0 or > 51)
            {
                throw new ArgumentException("The forward H.264 quantizer configuration is invalid.");
            }
            int skip = 16 - scanned.Length;
            int remainder = qp % 6;
            Span<int> scales = stackalloc int[3] { Even[remainder], Mixed[remainder], Odd[remainder] };
            int shift = 15 + qp / 6;
            long rounding = (1L << shift) / 3;
            for (int index = skip; index < 16; index++)
            {
                int position = Scan[index];
                scanned[index - skip] = Quantize(transformed[position], scales[ScanScales[index]], shift, rounding);
            }
        }

        public static void ChromaDc(ReadOnlySpan<int> dc, int qp, Span<int> levels)
        {
            if (dc.Length != 4 || levels.Length != 4 || qp is < 0 or > 51)
            {
                throw new ArgumentException("The forward H.264 chroma DC transform requires four coefficients.");
            }
            int a = dc[0] + dc[1];
            int b = dc[0] - dc[1];
            int c = dc[2] + dc[3];
            int d = dc[2] - dc[3];
            int shift = 16 + qp / 6;
            int scale = Even[qp % 6];
            long rounding = (1L << shift) / 3;
            levels[0] = Quantize(a + c, scale, shift, rounding);
            levels[1] = Quantize(b + d, scale, shift, rounding);
            levels[2] = Quantize(a - c, scale, shift, rounding);
            levels[3] = Quantize(b - d, scale, shift, rounding);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Quantize(int value, int scale, int shift, long rounding)
        {
            long magnitude = ((long)Math.Abs(value) * scale + rounding) >> shift;
            return checked((int)(value < 0 ? -magnitude : magnitude));
        }

        private static void TransformSse2(ReadOnlySpan<int> residual, Span<int> transformed)
        {
            Vector128<int> first = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(residual));
            Vector128<int> second = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(residual), 4);
            Vector128<int> third = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(residual), 8);
            Vector128<int> fourth = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(residual), 12);
            Transpose(ref first, ref second, ref third, ref fourth);
            TransformVectors(ref first, ref second, ref third, ref fourth);
            Transpose(ref first, ref second, ref third, ref fourth);
            TransformVectors(ref first, ref second, ref third, ref fourth);
            first.StoreUnsafe(ref MemoryMarshal.GetReference(transformed));
            second.StoreUnsafe(ref MemoryMarshal.GetReference(transformed), 4);
            third.StoreUnsafe(ref MemoryMarshal.GetReference(transformed), 8);
            fourth.StoreUnsafe(ref MemoryMarshal.GetReference(transformed), 12);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TransformVectors(ref Vector128<int> first, ref Vector128<int> second,
            ref Vector128<int> third, ref Vector128<int> fourth)
        {
            Vector128<int> outerSum = first + fourth;
            Vector128<int> innerSum = second + third;
            Vector128<int> innerDifference = second - third;
            Vector128<int> outerDifference = first - fourth;
            first = outerSum + innerSum;
            second = Vector128.ShiftLeft(outerDifference, 1) + innerDifference;
            third = outerSum - innerSum;
            fourth = outerDifference - Vector128.ShiftLeft(innerDifference, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose(ref Vector128<int> first, ref Vector128<int> second,
            ref Vector128<int> third, ref Vector128<int> fourth)
        {
            Vector128<long> upperLeft = Sse2.UnpackLow(first, second).AsInt64();
            Vector128<long> upperRight = Sse2.UnpackHigh(first, second).AsInt64();
            Vector128<long> lowerLeft = Sse2.UnpackLow(third, fourth).AsInt64();
            Vector128<long> lowerRight = Sse2.UnpackHigh(third, fourth).AsInt64();
            first = Sse2.UnpackLow(upperLeft, lowerLeft).AsInt32();
            second = Sse2.UnpackHigh(upperLeft, lowerLeft).AsInt32();
            third = Sse2.UnpackLow(upperRight, lowerRight).AsInt32();
            fourth = Sse2.UnpackHigh(upperRight, lowerRight).AsInt32();
        }

        private static void TransformLine(Span<int> values, int offset, int stride)
        {
            int a = values[offset] + values[offset + stride * 3];
            int b = values[offset + stride] + values[offset + stride * 2];
            int c = values[offset + stride] - values[offset + stride * 2];
            int d = values[offset] - values[offset + stride * 3];
            values[offset] = a + b;
            values[offset + stride] = 2 * d + c;
            values[offset + stride * 2] = a - b;
            values[offset + stride * 3] = d - 2 * c;
        }
    }
}
