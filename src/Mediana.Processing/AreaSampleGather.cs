// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Processing
{
    internal readonly struct AreaSampleGather
    {
        internal const int Lanes = 8;
        private readonly int start;
        private readonly Vector128<byte> indices;
        private readonly Vector128<sbyte> weights;
        private readonly Vector128<byte> thirdIndices;
        private readonly Vector128<ushort> thirdWeights;

        public AreaSampleGather(ReadOnlySpan<AreaSampleRange> ranges, int inputWidth, long unit, long interior)
        {
            start = Math.Min(ranges[0].First, inputWidth - 16);
            Span<byte> offsets = stackalloc byte[16];
            Span<sbyte> factors = stackalloc sbyte[16];
            Span<byte> extraOffsets = stackalloc byte[16];
            Span<ushort> extraFactors = stackalloc ushort[Lanes];
            extraOffsets.Fill(0x80);
            extraFactors.Clear();
            for (int lane = 0; lane < Lanes; lane++)
            {
                AreaSampleRange range = ranges[lane];
                offsets[lane * 2] = (byte)(range.First - start);
                offsets[lane * 2 + 1] = (byte)(range.Last - start);
                factors[lane * 2] = (sbyte)(range.FirstWeight / unit);
                factors[lane * 2 + 1] = range.First == range.Last ? (sbyte)0 : (sbyte)(range.LastWeight / unit);
                if (range.Last - range.First == 2)
                {
                    extraOffsets[lane] = (byte)(range.First + 1 - start);
                    extraFactors[lane] = (ushort)(interior / unit);
                }
            }
            indices = Vector128.Create((ReadOnlySpan<byte>)offsets);
            weights = Vector128.Create((ReadOnlySpan<sbyte>)factors);
            thirdIndices = Vector128.Create((ReadOnlySpan<byte>)extraOffsets);
            thirdWeights = Vector128.Create((ReadOnlySpan<ushort>)extraFactors);
        }

        public Vector128<ushort> Filter(ReadOnlySpan<byte> row)
        {
            Vector128<byte> samples = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(row), (nuint)start);
            Vector128<ushort> result = Ssse3.MultiplyAddAdjacent(Ssse3.Shuffle(samples, indices), weights).AsUInt16();
            if (thirdWeights != Vector128<ushort>.Zero)
            {
                result += Vector128.WidenLower(Ssse3.Shuffle(samples, thirdIndices)) * thirdWeights;
            }
            return result;
        }
    }
}
