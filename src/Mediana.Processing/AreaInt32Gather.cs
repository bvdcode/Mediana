// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Processing
{
    internal readonly struct AreaInt32Gather
    {
        internal const int Lanes = 4;
        private readonly int start;
        private readonly bool hasThird;
        private readonly Vector128<byte> indices;
        private readonly Vector128<byte> extraIndices;
        private readonly Vector128<short> weights;

        public AreaInt32Gather(ReadOnlySpan<AreaSampleRange> columns, int sourceWidth)
        {
            start = Math.Min(columns[0].First, sourceWidth - 16);
            Span<byte> offsets = stackalloc byte[16];
            Span<byte> extras = stackalloc byte[16];
            Span<short> factors = stackalloc short[Lanes * 2];
            offsets.Fill(0x80);
            extras.Fill(0x80);
            for (int i = 0; i < Lanes; i++)
            {
                AreaSampleRange col = columns[i];
                offsets[2 * i] = (byte)(col.First - start);
                offsets[2 * i + 1] = (byte)(col.Last - start);
                factors[2 * i] = (short)col.FirstWeight;
                factors[2 * i + 1] = col.First == col.Last ? (short)0 : (short)col.LastWeight;
                if (col.Last - col.First == 2)
                {
                    extras[i] = (byte)(col.First + 1 - start);
                    hasThird = true;
                }
            }
            indices = Vector128.Create((ReadOnlySpan<byte>)offsets);
            extraIndices = Vector128.Create((ReadOnlySpan<byte>)extras);
            weights = Vector128.Create((ReadOnlySpan<short>)factors);
        }

        public Vector128<int> Filter(ReadOnlySpan<byte> row, int interiorWeight)
        {
            Vector128<byte> samples = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(row), (nuint)start);
            Vector128<byte> pairs = Ssse3.Shuffle(samples, indices);
            Vector128<int> filtered = Sse2.MultiplyAddAdjacent(Vector128.WidenLower(pairs).AsInt16(), weights);
            if (hasThird)
            {
                Vector128<ushort> extra = Vector128.WidenLower(Ssse3.Shuffle(samples, extraIndices));
                filtered += Vector128.WidenLower(extra).AsInt32() * interiorWeight;
            }
            return filtered;
        }
    }
}
