// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264LumaFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sum(ReadOnlySpan<byte> samples, int start, int stride) =>
            samples[start] + samples[start + 5 * stride] - 5 * (samples[start + stride] + samples[start + 4 * stride]) +
            20 * (samples[start + 2 * stride] + samples[start + 3 * stride]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<short> Sum8(ReadOnlySpan<byte> samples, int start, int stride) =>
            SumWide(samples, start, stride, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<short> Sum4(ReadOnlySpan<byte> samples, int start, int stride) =>
            SumWide(samples, start, stride, true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> SumWide(ReadOnlySpan<byte> samples, int start, int stride, bool four)
        {
            Vector128<short> outer = ReadSamples(samples, start, four) + ReadSamples(samples, start + 5 * stride, four);
            Vector128<short> middle = ReadSamples(samples, start + stride, four) + ReadSamples(samples, start + 4 * stride, four);
            Vector128<short> inner = ReadSamples(samples, start + 2 * stride, four) + ReadSamples(samples, start + 3 * stride, four);
            return outer - middle * Vector128.Create((short)5) + inner * Vector128.Create((short)20);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Half8(Vector128<short> sum)
        {
            Vector128<short> rounded = Vector128.ShiftRightArithmetic(sum + Vector128.Create((short)16), 5);
            if (Sse2.IsSupported)
            {
                return Sse2.PackUnsignedSaturate(rounded, Vector128<short>.Zero);
            }
            Vector128<ushort> clipped = Vector128.Clamp(rounded, Vector128<short>.Zero, Vector128.Create((short)255)).AsUInt16();
            return Vector128.Narrow(clipped, Vector128<ushort>.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Diagonal8(ReadOnlySpan<short> horizontal, int stride, int start)
        {
            // Byte-domain first-pass sums are between -2550 and 10710.
            Vector128<short> outer = Read8(horizontal, start) + Read8(horizontal, start + 5 * stride);
            Vector128<short> middle = Read8(horizontal, start + stride) + Read8(horizontal, start + 4 * stride);
            Vector128<short> inner = Read8(horizontal, start + 2 * stride) + Read8(horizontal, start + 3 * stride);
            Vector128<int> lower;
            Vector128<int> upper;
            if (Sse2.IsSupported)
            {
                Vector128<short> factors = Vector128.Create((short)-5, 20, -5, 20, -5, 20, -5, 20);
                lower = Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(middle, inner), factors) + Vector128.WidenLower(outer);
                upper = Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(middle, inner), factors) + Vector128.WidenUpper(outer);
            }
            else
            {
                lower = Vector128.WidenLower(outer) - Vector128.WidenLower(middle) * Vector128.Create(5)
                    + Vector128.WidenLower(inner) * Vector128.Create(20);
                upper = Vector128.WidenUpper(outer) - Vector128.WidenUpper(middle) * Vector128.Create(5)
                    + Vector128.WidenUpper(inner) * Vector128.Create(20);
            }
            lower = Vector128.ShiftRightArithmetic(lower + Vector128.Create(512), 10);
            upper = Vector128.ShiftRightArithmetic(upper + Vector128.Create(512), 10);
            return Pack(lower, upper);
        }

        public static int Diagonal(ReadOnlySpan<short> horizontal, int stride, int start)
        {
            int sum = horizontal[start] + horizontal[start + 5 * stride]
                - 5 * (horizontal[start + stride] + horizontal[start + 4 * stride])
                + 20 * (horizontal[start + 2 * stride] + horizontal[start + 3 * stride]);
            return Math.Clamp((sum + 512) >> 10, 0, 255);
        }

        private static Vector128<short> Read8(ReadOnlySpan<short> horizontal, int start) =>
            Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Diagonal8(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<int> lower = Diagonal4(horizontal, stride, start);
            Vector128<int> upper = Diagonal4(horizontal, stride, start + Vector128<int>.Count);
            return Pack(lower, upper);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> Diagonal4Samples(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            return Pack(Diagonal4(horizontal, stride, start), Vector128<int>.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Diagonal4(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<int> outer = Read4(horizontal, start) + Read4(horizontal, start + 5 * stride);
            Vector128<int> middle = Read4(horizontal, start + stride) + Read4(horizontal, start + 4 * stride);
            Vector128<int> inner = Read4(horizontal, start + 2 * stride) + Read4(horizontal, start + 3 * stride);
            Vector128<int> middleFive = middle + Vector128.ShiftLeft(middle, 2);
            Vector128<int> innerTwenty = Vector128.ShiftLeft(inner + Vector128.ShiftLeft(inner, 2), 2);
            Vector128<int> sum = outer - middleFive + innerTwenty;
            return Vector128.ShiftRightArithmetic(sum + Vector128.Create(512), 10);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> Pack(Vector128<int> lower, Vector128<int> upper)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.PackUnsignedSaturate(Sse2.PackSignedSaturate(lower, upper), Vector128<short>.Zero);
            }
            lower = Vector128.Clamp(lower, Vector128<int>.Zero, Vector128.Create(255));
            upper = Vector128.Clamp(upper, Vector128<int>.Zero, Vector128.Create(255));
            Vector128<ushort> clipped = Vector128.Narrow(lower, upper).AsUInt16();
            return Vector128.Narrow(clipped, Vector128<ushort>.Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> ReadSamples(ReadOnlySpan<byte> samples, int start, bool four)
        {
            Vector128<byte> value;
            if (four)
            {
                value = Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), start))).AsByte();
            }
            else
            {
                value = Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), start))).AsByte();
            }
            return Vector128.WidenLower(value).AsInt16();
        }

        private static Vector128<int> Read4(ReadOnlySpan<int> horizontal, int start) =>
            Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)start);
    }
}
