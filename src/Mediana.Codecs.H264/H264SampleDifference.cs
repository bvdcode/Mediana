// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264SampleDifference
    {
        public static int Sum16x16(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int stride, int stop)
        {
            int sum = 0;
            for (int row = 0; row < 16; row++)
            {
                Vector128<byte> left = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)(row * 16));
                Vector128<byte> right = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(prediction), (nuint)(row * stride));
                sum += Sum16(left, right);
                if (sum > stop)
                {
                    break;
                }
            }
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sum16(Vector128<byte> left, Vector128<byte> right)
        {
            if (Sse2.IsSupported)
            {
                Vector128<ushort> sums = Sse2.SumAbsoluteDifferences(left, right);
                return sums.GetElement(0) + sums.GetElement(4);
            }
            Vector128<byte> difference = Vector128.Subtract(Vector128.Max(left, right), Vector128.Min(left, right));
            return Vector128.Sum(Vector128.WidenLower(difference)) + Vector128.Sum(Vector128.WidenUpper(difference));
        }
    }
}
