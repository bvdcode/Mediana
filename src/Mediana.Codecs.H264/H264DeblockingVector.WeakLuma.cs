// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static partial class H264DeblockingVector
    {
        internal static void WeakLuma16(Span<byte> pixels, int position, int stride, bool vertical, uint groups,
            in H264DeblockingThresholds thresholds)
        {
            for (int half = 0; half < 2; half++, groups >>= 16)
            {
                short first = (byte)groups;
                short second = (byte)(groups >> 8);
                if ((first | second) == 0)
                {
                    continue;
                }
                int origin = position + half * 8 * (vertical ? stride : 1);
                WeakLuma8(pixels, origin, stride, vertical, first, second, thresholds);
            }
        }

        private static void WeakLuma8(Span<byte> pixels, int position, int stride, bool vertical,
            short first, short second, in H264DeblockingThresholds thresholds)
        {
            H264DeblockingSamples samples = Read(pixels, position, stride, vertical, strongTaps: false);
            Vector128<short> strengths = Vector128.Create(first, first, first, first, second, second, second, second);
            Vector128<short> beta = Vector128.Create((short)thresholds.Beta);
            Vector128<short> enabled = Enabled(samples, strengths, beta,
                Vector128.Abs(samples.P0 - samples.Q0), thresholds.Alpha);
            if (enabled.ExtractMostSignificantBits() == 0)
            {
                return;
            }
            short a = thresholds.Clipping(first);
            short b = thresholds.Clipping(second);
            Vector128<short> clipping = Vector128.Create(a, a, a, a, b, b, b, b);
            Normal(ref samples, enabled, clipping, beta, chroma: false);
            WriteWeakLuma(pixels, position, stride, vertical, samples);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteWeakLuma(Span<byte> pixels, int position, int stride, bool vertical, in H264DeblockingSamples samples)
        {
            if (!vertical)
            {
                Write8(pixels, position - 2 * stride, samples.P1);
                Write8(pixels, position - stride, samples.P0);
                Write8(pixels, position, samples.Q0);
                Write8(pixels, position + stride, samples.Q1);
                return;
            }
            Vector128<byte> p = Sse2.PackUnsignedSaturate(samples.P1, samples.P0);
            Vector128<byte> q = Sse2.PackUnsignedSaturate(samples.Q0, samples.Q1);
            Vector128<ushort> pPairs = Sse2.UnpackLow(p, Sse2.ShiftRightLogical128BitLane(p, 8)).AsUInt16();
            Vector128<ushort> qPairs = Sse2.UnpackLow(q, Sse2.ShiftRightLogical128BitLane(q, 8)).AsUInt16();
            Vector128<uint> top = Sse2.UnpackLow(pPairs, qPairs).AsUInt32();
            Vector128<uint> bottom = Sse2.UnpackHigh(pPairs, qPairs).AsUInt32();
            ref byte first = ref Unsafe.Add(ref MemoryMarshal.GetReference(pixels), position - 2);
            Unsafe.WriteUnaligned(ref first, top.GetElement(0));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, stride), top.GetElement(1));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 2 * stride), top.GetElement(2));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 3 * stride), top.GetElement(3));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 4 * stride), bottom.GetElement(0));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 5 * stride), bottom.GetElement(1));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 6 * stride), bottom.GetElement(2));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 7 * stride), bottom.GetElement(3));
        }
    }
}
