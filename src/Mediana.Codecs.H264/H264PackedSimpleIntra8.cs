// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264PackedSimpleIntra8
    {
        private static Vector128<byte> Preceding =>
            Vector128.Create((byte)128, 0, 1, 2, 3, 4, 5, 6, 128, 8, 9, 10, 11, 12, 13, 14);
        private static Vector128<byte> Following =>
            Vector128.Create((byte)1, 2, 3, 4, 5, 6, 7, 128, 9, 10, 11, 12, 13, 14, 15, 128);

        internal static void Write(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output, int stride, int delta)
        {
            bool above = mode != 1 && !top.IsEmpty;
            bool beside = mode != 0 && !left.IsEmpty;
            delta = Math.Clamp(delta, -255, 255);
            ulong high = above ? MemoryMarshal.Read<ulong>(top) : 0;
            ulong low = beside ? MemoryMarshal.Read<ulong>(left) : 0;
            ulong previousAbove = above ? corner ?? top[0] : 0UL;
            ulong previousLeft = beside ? corner ?? left[0] : 0UL;
            ulong nextAbove = above ? top[Math.Min(8, top.Length - 1)] : 0UL;
            ulong nextLeft = beside ? left[7] : 0UL;
            Vector128<byte> current = Vector128.Create(high, low).AsByte();
            Vector128<byte> previous = Vector128.ShuffleNative(current, Preceding)
                | Vector128.Create(previousAbove, previousLeft).AsByte();
            Vector128<byte> next = Vector128.ShuffleNative(current, Following)
                | Vector128.Create(nextAbove << 56, nextLeft << 56).AsByte();
            Vector128<byte> filtered = H264SampleAverage.Quarter(previous, current, next);

            if (mode == 2)
            {
                int sum = H264SampleDifference.Sum16(filtered, Vector128<byte>.Zero);
                int prediction = above && beside ? (sum + 8) >> 4 : above || beside ? (sum + 4) >> 3 : 128;
                ulong dc = Repeat((byte)Math.Clamp(prediction + delta, 0, 255));
                WriteRows(dc, output, stride);
                return;
            }
            if (delta > 0)
            {
                filtered = Vector128.AddSaturate(filtered, Vector128.Create((byte)delta));
            }
            else if (delta < 0)
            {
                filtered = Vector128.SubtractSaturate(filtered, Vector128.Create((byte)-delta));
            }
            if (mode == 0)
            {
                WriteRows(filtered.AsUInt64().GetElement(0), output, stride);
                return;
            }
            ulong horizontal = filtered.AsUInt64().GetElement(1);
            for (int row = 0; row < 8; row++)
            {
                ulong value = Repeat((byte)horizontal);
                MemoryMarshal.Write(output[(row * stride)..], in value);
                horizontal >>= 8;
            }
        }

        private static void WriteRows(ulong row, Span<byte> output, int stride)
        {
            for (int index = 0; index < 8; index++)
            {
                MemoryMarshal.Write(output[(index * stride)..], in row);
            }
        }

        private static ulong Repeat(byte value) => (ulong)value * 0x0101010101010101UL;
    }
}
