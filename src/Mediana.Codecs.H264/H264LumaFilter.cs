// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264LumaFilter
    {
        public static Vector128<short> Sum8(ReadOnlySpan<byte> samples, int start, int stride)
        {
            Vector128<short> outer = Read8(samples, start) + Read8(samples, start + 5 * stride);
            Vector128<short> middle = Read8(samples, start + stride) + Read8(samples, start + 4 * stride);
            Vector128<short> inner = Read8(samples, start + 2 * stride) + Read8(samples, start + 3 * stride);
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

        private static Vector128<int> Diagonal4(ReadOnlySpan<int> horizontal, int stride, int start)
        {
            Vector128<int> outer = Read4(horizontal, start) + Read4(horizontal, start + 5 * stride);
            Vector128<int> middle = Read4(horizontal, start + stride) + Read4(horizontal, start + 4 * stride);
            Vector128<int> inner = Read4(horizontal, start + 2 * stride) + Read4(horizontal, start + 3 * stride);
            Vector128<int> sum = outer - middle * Vector128.Create(5) + inner * Vector128.Create(20);
            Vector128<int> rounded = Vector128.ShiftRightArithmetic(sum + Vector128.Create(512), 10);
            return Vector128.Clamp(rounded, Vector128<int>.Zero, Vector128.Create(255));
        }

        private static Vector128<short> Read8(ReadOnlySpan<byte> samples, int start) =>
            Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(
                ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), start))).AsByte()).AsInt16();

        private static Vector128<int> Read4(ReadOnlySpan<int> horizontal, int start) =>
            Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)start);
    }
}
