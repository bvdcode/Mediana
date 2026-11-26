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
        public static int SumPackedMacroblock(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction)
        {
            Vector128<ushort> partial = Vector128<ushort>.Zero;
            ref byte left = ref MemoryMarshal.GetReference(source[..H264SourceMacroblock.ByteCount]);
            ref byte right = ref MemoryMarshal.GetReference(prediction[..H264SourceMacroblock.ByteCount]);
            for (nuint i = 0; i < H264SourceMacroblock.ByteCount; i += (nuint)Vector128<byte>.Count)
            {
                partial += Accumulate16(Vector128.LoadUnsafe(ref left, i), Vector128.LoadUnsafe(ref right, i));
            }
            return Sse2.IsSupported
                ? Total(partial)
                : (int)Vector128.Sum(Vector128.WidenLower(partial) + Vector128.WidenUpper(partial));
        }

        public static int Sum16x16(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int stride, int stop)
        {
            int sum = 0;
            for (int row = 0; row < 16; row += 4)
            {
                Vector128<ushort> partial = Vector128<ushort>.Zero;
                for (int offset = 0; offset < 4; offset++)
                {
                    Vector128<byte> left = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)((row + offset) * 16));
                    Vector128<byte> right = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(prediction), (nuint)((row + offset) * stride));
                    partial += Accumulate16(left, right);
                }
                sum += Total(partial);
                if (sum > stop)
                {
                    break;
                }
            }
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SumSquared16(Vector128<byte> left, Vector128<byte> right)
        {
            Vector128<short> low = Vector128.WidenLower(left).AsInt16() - Vector128.WidenLower(right).AsInt16();
            Vector128<short> high = Vector128.WidenUpper(left).AsInt16() - Vector128.WidenUpper(right).AsInt16();
            if (Sse2.IsSupported)
            {
                return Vector128.Sum(Sse2.MultiplyAddAdjacent(low, low) + Sse2.MultiplyAddAdjacent(high, high));
            }
            Vector128<int> a = Vector128.WidenLower(low);
            Vector128<int> b = Vector128.WidenUpper(low);
            Vector128<int> c = Vector128.WidenLower(high);
            Vector128<int> d = Vector128.WidenUpper(high);
            return Vector128.Sum(a * a + b * b + c * c + d * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sum16(Vector128<byte> left, Vector128<byte> right) => Total(Accumulate16(left, right));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<ushort> Accumulate16(Vector128<byte> left, Vector128<byte> right)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.SumAbsoluteDifferences(left, right);
            }
            Vector128<byte> difference = Vector128.Subtract(Vector128.Max(left, right), Vector128.Min(left, right));
            return Vector128.WidenLower(difference) + Vector128.WidenUpper(difference);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Total(Vector128<ushort> sums) => Sse2.IsSupported
            ? sums.GetElement(0) + sums.GetElement(4) : Vector128.Sum(sums);
    }
}
