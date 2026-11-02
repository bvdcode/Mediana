// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static partial class H264ForwardTransform
    {
        private static ReadOnlySpan<int> Scan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<byte> ScanScales => [0, 1, 1, 0, 2, 0, 1, 1, 1, 1, 2, 0, 2, 1, 1, 2];
        private static ReadOnlySpan<int> Even => [13107, 11916, 10082, 9362, 8192, 7282];
        private static ReadOnlySpan<int> Mixed => [8066, 7490, 6554, 5825, 5243, 4559];
        private static ReadOnlySpan<int> Odd => [5243, 4660, 4194, 3647, 3355, 2893];

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
            if (Sse2.IsSupported)
            {
                QuantizeSse2(transformed, scales, shift, rounding, scanned);
                return;
            }
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

        private static void QuantizeSse2(ReadOnlySpan<int> transformed, ReadOnlySpan<int> scales,
            int shift, long rounding, Span<int> scanned)
        {
            Vector128<int> first = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(transformed));
            Vector128<int> second = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(transformed), 4);
            Vector128<int> third = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(transformed), 8);
            Vector128<int> fourth = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(transformed), 12);
            int skip = 16 - scanned.Length;
            if (skip != 0)
            {
                first &= Vector128.Create(0, -1, -1, -1);
            }
            Vector128<int> minimum = Vector128.Create(int.MinValue);
            Vector128<int> invalid = Vector128.Equals(first, minimum) | Vector128.Equals(second, minimum) |
                Vector128.Equals(third, minimum) | Vector128.Equals(fourth, minimum);
            if (invalid.ExtractMostSignificantBits() != 0)
            {
                throw new OverflowException("The forward H.264 quantizer cannot represent the minimum integer magnitude.");
            }
            Vector128<uint> even = Vector128.Create((uint)scales[0]);
            Vector128<uint> mixed = Vector128.Create((uint)scales[1]);
            Vector128<uint> odd = Vector128.Create((uint)scales[2]);
            Vector128<ulong> bias = Vector128.Create((ulong)rounding);
            Vector128<ulong> shiftCount = Vector128.CreateScalar((ulong)shift);
            first = QuantizeRow(first, even, mixed, bias, shiftCount);
            second = QuantizeRow(second, mixed, odd, bias, shiftCount);
            third = QuantizeRow(third, even, mixed, bias, shiftCount);
            fourth = QuantizeRow(fourth, mixed, odd, bias, shiftCount);
            WriteQuantizedScan(first, second, third, fourth, scanned);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteQuantizedScan(Vector128<int> first, Vector128<int> second,
            Vector128<int> third, Vector128<int> fourth, Span<int> scanned)
        {
            Vector128<int> firstGroup = Sse2.UnpackLow(first.AsInt64(), Sse2.UnpackLow(second, third).AsInt64()).AsInt32();
            Vector128<int> secondGroup = Sse2.Shuffle(Sse.Shuffle(first.AsSingle(), second.AsSingle(), 0x9E).AsInt32(), 0xD2);
            Vector128<int> thirdGroup = Sse2.Shuffle(Sse.Shuffle(third.AsSingle(), fourth.AsSingle(), 0x49).AsInt32(), 0x78);
            Vector128<int> fourthGroup = Sse2.UnpackHigh(Sse2.UnpackHigh(second, third).AsInt64(), fourth.AsInt64()).AsInt32();
            if (scanned.Length == 16)
            {
                firstGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned));
                secondGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 4);
                thirdGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 8);
                fourthGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 12);
            }
            else
            {
                Sse2.ShiftRightLogical128BitLane(firstGroup, 4).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(scanned));
                scanned[2] = firstGroup.GetElement(3);
                secondGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 3);
                thirdGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 7);
                fourthGroup.StoreUnsafe(ref MemoryMarshal.GetReference(scanned), 11);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> QuantizeRow(Vector128<int> values, Vector128<uint> evenScale,
            Vector128<uint> oddScale, Vector128<ulong> bias, Vector128<ulong> shift)
        {
            Vector128<int> sign = Vector128.ShiftRightArithmetic(values, 31);
            Vector128<uint> magnitude = ((values ^ sign) - sign).AsUInt32();
            Vector128<ulong> even = Sse2.ShiftRightLogical(Sse2.Multiply(magnitude, evenScale) + bias, shift);
            Vector128<uint> oddMagnitude = Sse2.ShiftRightLogical128BitLane(magnitude, 4);
            Vector128<ulong> odd = Sse2.ShiftRightLogical(Sse2.Multiply(oddMagnitude, oddScale) + bias, shift);
            Vector128<ulong> low = Sse2.UnpackLow(even.AsUInt32(), odd.AsUInt32()).AsUInt64();
            Vector128<ulong> high = Sse2.UnpackHigh(even.AsUInt32(), odd.AsUInt32()).AsUInt64();
            Vector128<int> levels = Sse2.UnpackLow(low, high).AsInt32();
            return (levels ^ sign) - sign;
        }
    }
}
