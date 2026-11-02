// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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

        public static Vector64<byte> Half8(Vector128<short> sum)
        {
            Vector128<short> rounded = Vector128.ShiftRightArithmetic(sum + Vector128.Create((short)16), 5);
            Vector128<ushort> clipped = Vector128.Clamp(rounded, Vector128<short>.Zero, Vector128.Create((short)255)).AsUInt16();
            return Vector128.Narrow(clipped, Vector128<ushort>.Zero).GetLower();
        }

        public static Vector64<byte> Diagonal8(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<int> lower = Diagonal4(horizontal, stride, start);
            Vector128<int> upper = Diagonal4(horizontal, stride, start + Vector128<int>.Count);
            Vector128<ushort> clipped = Vector128.Narrow(lower, upper).AsUInt16();
            return Vector128.Narrow(clipped, Vector128<ushort>.Zero).GetLower();
        }

        public static Vector128<byte> Diagonal4Samples(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<ushort> clipped = Vector128.Narrow(Diagonal4(horizontal, stride, start), Vector128<int>.Zero).AsUInt16();
            return Vector128.Narrow(clipped, Vector128<ushort>.Zero);
        }

        private static Vector128<int> Diagonal4(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<int> outer = Read4(horizontal, start) + Read4(horizontal, start + 5 * stride);
            Vector128<int> middle = Read4(horizontal, start + stride) + Read4(horizontal, start + 4 * stride);
            Vector128<int> inner = Read4(horizontal, start + 2 * stride) + Read4(horizontal, start + 3 * stride);
            Vector128<int> sum = outer - middle * Vector128.Create(5) + inner * Vector128.Create(20);
            Vector128<int> rounded = Vector128.ShiftRightArithmetic(sum + Vector128.Create(512), 10);
            return Vector128.Clamp(rounded, Vector128<int>.Zero, Vector128.Create(255));
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
