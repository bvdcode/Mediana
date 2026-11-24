// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264QuarterLuma
    {
        public const int Scale = 4;

        public static void Reduce(ReadOnlySpan<byte> samples, int width, int height, Span<byte> output,
            CancellationToken token)
        {
            for (int y = 0; y < height; y += Scale)
            {
                token.ThrowIfCancellationRequested();
                int destination = y / Scale * (width / Scale);
                for (int x = 0; x < width; x += 16)
                {
                    uint row = AverageFourGroups(samples, y * width + x, width);
                    Unsafe.WriteUnaligned(ref output[destination + x / Scale], row);
                }
            }
        }

        public static Vector128<byte> ReduceMacroblock(ReadOnlySpan<byte> samples) =>
            Vector128.Create(AverageFourGroups(samples, 0, 16), AverageFourGroups(samples, 64, 16),
                AverageFourGroups(samples, 128, 16), AverageFourGroups(samples, 192, 16)).AsByte();

        private static uint AverageFourGroups(ReadOnlySpan<byte> samples, int offset, int stride)
        {
            Vector128<uint> sum = SumGroups(samples, offset) + SumGroups(samples, offset + stride)
                + SumGroups(samples, offset + 2 * stride) + SumGroups(samples, offset + 3 * stride);
            Vector128<uint> rounded = (sum + Vector128.Create(8u)) >> 4;
            Vector128<ushort> words = Vector128.Narrow(rounded, Vector128<uint>.Zero);
            return Vector128.Narrow(words, Vector128<ushort>.Zero).AsUInt32().ToScalar();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<uint> SumGroups(ReadOnlySpan<byte> samples, int offset)
        {
            Vector128<byte> row = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(samples), (nuint)offset);
            if (Ssse3.IsSupported)
            {
                Vector128<short> pairs = Ssse3.MultiplyAddAdjacent(row, Vector128<sbyte>.One);
                return Sse2.MultiplyAddAdjacent(pairs, Vector128<short>.One).AsUInt32();
            }
            Vector128<ushort> pairsOfBytes = row.AsUInt16();
            Vector128<uint> pairsOfSums = ((pairsOfBytes & Vector128.Create((ushort)255)) + (pairsOfBytes >> 8)).AsUInt32();
            return (pairsOfSums & Vector128.Create(65535u)) + (pairsOfSums >> 16);
        }
    }
}
