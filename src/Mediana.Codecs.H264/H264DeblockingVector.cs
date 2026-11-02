// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264DeblockingVector
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Filter(ref H264DeblockingSamples samples, Vector128<short> strengths,
            Vector128<short> clipping, in H264DeblockingThresholds thresholds, bool chroma)
        {
            Vector128<short> beta = Vector128.Create((short)thresholds.Beta);
            Vector128<short> difference = Vector128.Abs(samples.P0 - samples.Q0);
            Vector128<short> enabled = Vector128.GreaterThan(strengths, Vector128<short>.Zero)
                & Vector128.LessThan(difference, Vector128.Create((short)thresholds.Alpha))
                & Vector128.LessThan(Vector128.Abs(samples.P1 - samples.P0), beta)
                & Vector128.LessThan(Vector128.Abs(samples.Q1 - samples.Q0), beta);
            if (enabled.ExtractMostSignificantBits() == 0)
            {
                return false;
            }
            Vector128<short> strong = Vector128.Equals(strengths, Vector128.Create((short)4)) & enabled;
            Vector128<short> normal = Vector128.LessThan(strengths, Vector128.Create((short)4)) & enabled;
            if (normal.ExtractMostSignificantBits() != 0)
            {
                Normal(ref samples, normal, clipping, beta, chroma);
            }
            if (strong.ExtractMostSignificantBits() != 0)
            {
                Strong(ref samples, strong, difference, thresholds, chroma);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Normal(ref H264DeblockingSamples samples, Vector128<short> mask,
            Vector128<short> clipping, Vector128<short> beta, bool chroma)
        {
            Vector128<short> p0 = samples.P0;
            Vector128<short> q0 = samples.Q0;
            Vector128<short> ap = Vector128.LessThan(Vector128.Abs(samples.P2 - p0), beta);
            Vector128<short> aq = Vector128.LessThan(Vector128.Abs(samples.Q2 - q0), beta);
            Vector128<short> tc = chroma ? clipping + Vector128.Create((short)1) : clipping - ap - aq;
            Vector128<short> delta = Vector128.ShiftRightArithmetic(
                Vector128.ShiftLeft(q0 - p0, 2) + samples.P1 - samples.Q1 + Vector128.Create((short)4), 3);
            delta = Clip(delta, tc);
            samples.P0 = Vector128.ConditionalSelect(mask, p0 + delta, p0);
            samples.Q0 = Vector128.ConditionalSelect(mask, q0 - delta, q0);
            if (!chroma)
            {
                Vector128<short> average = Vector128.ShiftRightArithmetic(p0 + q0 + Vector128.Create((short)1), 1);
                Vector128<short> dp = Vector128.ShiftRightArithmetic(samples.P2 + average - Vector128.ShiftLeft(samples.P1, 1), 1);
                Vector128<short> dq = Vector128.ShiftRightArithmetic(samples.Q2 + average - Vector128.ShiftLeft(samples.Q1, 1), 1);
                samples.P1 = Vector128.ConditionalSelect(mask & ap, samples.P1 + Clip(dp, clipping), samples.P1);
                samples.Q1 = Vector128.ConditionalSelect(mask & aq, samples.Q1 + Clip(dq, clipping), samples.Q1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Strong(ref H264DeblockingSamples samples, Vector128<short> mask,
            Vector128<short> difference, in H264DeblockingThresholds thresholds, bool chroma)
        {
            Vector128<short> p2 = samples.P2;
            Vector128<short> p1 = samples.P1;
            Vector128<short> p0 = samples.P0;
            Vector128<short> q0 = samples.Q0;
            Vector128<short> q1 = samples.Q1;
            Vector128<short> q2 = samples.Q2;
            Vector128<short> two = Vector128.Create((short)2);
            Vector128<short> four = Vector128.Create((short)4);
            Vector128<short> filteredP0 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(p1, 1) + p0 + q1 + two, 2);
            Vector128<short> filteredQ0 = Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(q1, 1) + q0 + p1 + two, 2);
            if (!chroma)
            {
                Vector128<short> beta = Vector128.Create((short)thresholds.Beta);
                Vector128<short> wide = Vector128.LessThan(difference, Vector128.Create((short)((thresholds.Alpha >> 2) + 2))) & mask;
                Vector128<short> ap = Vector128.LessThan(Vector128.Abs(p2 - p0), beta) & wide;
                Vector128<short> aq = Vector128.LessThan(Vector128.Abs(q2 - q0), beta) & wide;
                filteredP0 = Vector128.ConditionalSelect(ap,
                    Vector128.ShiftRightArithmetic(p2 + Vector128.ShiftLeft(p1 + p0 + q0, 1) + q1 + four, 3), filteredP0);
                filteredQ0 = Vector128.ConditionalSelect(aq,
                    Vector128.ShiftRightArithmetic(p1 + Vector128.ShiftLeft(p0 + q0 + q1, 1) + q2 + four, 3), filteredQ0);
                samples.P1 = Vector128.ConditionalSelect(ap, Vector128.ShiftRightArithmetic(p2 + p1 + p0 + q0 + two, 2), p1);
                samples.Q1 = Vector128.ConditionalSelect(aq, Vector128.ShiftRightArithmetic(p0 + q0 + q1 + q2 + two, 2), q1);
                samples.P2 = Vector128.ConditionalSelect(ap,
                    Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(samples.P3 + p2, 1) + p2 + p1 + p0 + q0 + four, 3), p2);
                samples.Q2 = Vector128.ConditionalSelect(aq,
                    Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(samples.Q3 + q2, 1) + q2 + q1 + q0 + p0 + four, 3), q2);
            }
            samples.P0 = Vector128.ConditionalSelect(mask, filteredP0, p0);
            samples.Q0 = Vector128.ConditionalSelect(mask, filteredQ0, q0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> Clip(Vector128<short> value, Vector128<short> limit) =>
            Vector128.Min(Vector128.Max(value, -limit), limit);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static H264DeblockingSamples Read(ReadOnlySpan<byte> pixels, int position, int stride, bool vertical)
        {
            if (!vertical)
            {
                return new()
                {
                    P3 = Read8(pixels, position - 4 * stride),
                    P2 = Read8(pixels, position - 3 * stride),
                    P1 = Read8(pixels, position - 2 * stride),
                    P0 = Read8(pixels, position - stride),
                    Q0 = Read8(pixels, position),
                    Q1 = Read8(pixels, position + stride),
                    Q2 = Read8(pixels, position + 2 * stride),
                    Q3 = Read8(pixels, position + 3 * stride)
                };
            }
            int left = position - 4;
            Vector128<byte> first = Vector128.Create(ReadRow(pixels, left), ReadRow(pixels, left + 4 * stride));
            Vector128<byte> second = Vector128.Create(ReadRow(pixels, left + stride), ReadRow(pixels, left + 5 * stride));
            Vector128<byte> third = Vector128.Create(ReadRow(pixels, left + 2 * stride), ReadRow(pixels, left + 6 * stride));
            Vector128<byte> fourth = Vector128.Create(ReadRow(pixels, left + 3 * stride), ReadRow(pixels, left + 7 * stride));
            Transpose(ref first, ref second, ref third, ref fourth);
            return new()
            {
                P3 = Vector128.WidenLower(first).AsInt16(),
                P2 = Vector128.WidenUpper(first).AsInt16(),
                P1 = Vector128.WidenLower(second).AsInt16(),
                P0 = Vector128.WidenUpper(second).AsInt16(),
                Q0 = Vector128.WidenLower(third).AsInt16(),
                Q1 = Vector128.WidenUpper(third).AsInt16(),
                Q2 = Vector128.WidenLower(fourth).AsInt16(),
                Q3 = Vector128.WidenUpper(fourth).AsInt16()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(Span<byte> pixels, int position, int stride, bool vertical, in H264DeblockingSamples samples)
        {
            if (!vertical)
            {
                Write8(pixels, position - 3 * stride, samples.P2);
                Write8(pixels, position - 2 * stride, samples.P1);
                Write8(pixels, position - stride, samples.P0);
                Write8(pixels, position, samples.Q0);
                Write8(pixels, position + stride, samples.Q1);
                Write8(pixels, position + 2 * stride, samples.Q2);
                return;
            }
            Vector128<byte> first = Sse2.PackUnsignedSaturate(samples.P3, samples.Q0);
            Vector128<byte> second = Sse2.PackUnsignedSaturate(samples.P2, samples.Q1);
            Vector128<byte> third = Sse2.PackUnsignedSaturate(samples.P1, samples.Q2);
            Vector128<byte> fourth = Sse2.PackUnsignedSaturate(samples.P0, samples.Q3);
            Transpose(ref first, ref second, ref third, ref fourth);
            int left = position - 4;
            first.GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)left);
            first.GetUpper().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + stride));
            second.GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 2 * stride));
            second.GetUpper().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 3 * stride));
            third.GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 4 * stride));
            third.GetUpper().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 5 * stride));
            fourth.GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 6 * stride));
            fourth.GetUpper().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(left + 7 * stride));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector64<byte> ReadRow(ReadOnlySpan<byte> pixels, int position) =>
            Vector64.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> Read8(ReadOnlySpan<byte> pixels, int position) =>
            Vector128.WidenLower(ReadRow(pixels, position).ToVector128Unsafe()).AsInt16();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write8(Span<byte> pixels, int position, Vector128<short> value) =>
            Sse2.PackUnsignedSaturate(value, Vector128<short>.Zero).GetLower().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose(ref Vector128<byte> first, ref Vector128<byte> second,
            ref Vector128<byte> third, ref Vector128<byte> fourth)
        {
            Vector128<ushort> topLeft = Sse2.UnpackLow(first, second).AsUInt16();
            Vector128<ushort> bottomLeft = Sse2.UnpackHigh(first, second).AsUInt16();
            Vector128<ushort> topRight = Sse2.UnpackLow(third, fourth).AsUInt16();
            Vector128<ushort> bottomRight = Sse2.UnpackHigh(third, fourth).AsUInt16();
            Vector128<uint> upper = Sse2.UnpackLow(topLeft, topRight).AsUInt32();
            Vector128<uint> lower = Sse2.UnpackHigh(topLeft, topRight).AsUInt32();
            Vector128<uint> upperBottom = Sse2.UnpackLow(bottomLeft, bottomRight).AsUInt32();
            Vector128<uint> lowerBottom = Sse2.UnpackHigh(bottomLeft, bottomRight).AsUInt32();
            first = Sse2.UnpackLow(upper, upperBottom).AsByte();
            second = Sse2.UnpackHigh(upper, upperBottom).AsByte();
            third = Sse2.UnpackLow(lower, lowerBottom).AsByte();
            fourth = Sse2.UnpackHigh(lower, lowerBottom).AsByte();
        }
    }
}
