// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcPredictionWeights(int lumaDenominator, int chromaDenominator,
        HevcPredictionWeight[] list0, HevcPredictionWeight[] list1)
    {
        public int LumaDenominator => lumaDenominator;
        public int ChromaDenominator => chromaDenominator;
        public IReadOnlyList<HevcPredictionWeight> List0 { get; } = Array.AsReadOnly(list0);
        public IReadOnlyList<HevcPredictionWeight> List1 { get; } = Array.AsReadOnly(list1);

        public static HevcPredictionWeights Read(BitReader reader, bool chroma, int count0, int count1)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count0, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count0, 15);
            ArgumentOutOfRangeException.ThrowIfNegative(count1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count1, 15);
            int luma = HevcSyntax.Ue(reader, 7, "luma_log2_weight_denom");
            int chromaDenom = chroma ? luma + HevcSyntax.Se(reader, -luma, 7 - luma, "delta_chroma_log2_weight_denom") : luma;
            int flags = 0;
            HevcPredictionWeight[] first = ReadList(reader, chroma, count0, luma, chromaDenom, ref flags);
            HevcPredictionWeight[] second = ReadList(reader, chroma, count1, luma, chromaDenom, ref flags);
            return new(luma, chromaDenom, first, second);
        }

        private static HevcPredictionWeight[] ReadList(BitReader reader, bool chroma, int count,
            int lumaDenom, int chromaDenom, ref int flagCount)
        {
            Span<bool> lumaFlags = stackalloc bool[count], chromaFlags = stackalloc bool[count];
            chromaFlags.Clear();
            for (int i = 0; i < count; i++) { lumaFlags[i] = reader.ReadFlag(); if (lumaFlags[i]) { flagCount++; } }
            if (chroma)
            {
                for (int i = 0; i < count; i++) { chromaFlags[i] = reader.ReadFlag(); if (chromaFlags[i]) { flagCount += 2; } }
            }
            if (flagCount > 24) { throw new InvalidDataException("HEVC prediction weights exceed the signalled component limit."); }
            HevcPredictionWeight[] weights = new HevcPredictionWeight[count];
            for (int i = 0; i < count; i++)
            {
                int lumaWeight = 1 << lumaDenom, lumaOffset = 0, cbWeight = 1 << chromaDenom, crWeight = cbWeight;
                int cbOffset = 0, crOffset = 0;
                if (lumaFlags[i])
                {
                    lumaWeight += HevcSyntax.Se(reader, -128, 127, "delta_luma_weight");
                    lumaOffset = HevcSyntax.Se(reader, -128, 127, "luma_offset");
                }
                if (chromaFlags[i])
                {
                    cbWeight += HevcSyntax.Se(reader, -128, 127, "delta_chroma_weight");
                    cbOffset = ReadChromaOffset(reader, cbWeight, chromaDenom);
                    crWeight += HevcSyntax.Se(reader, -128, 127, "delta_chroma_weight");
                    crOffset = ReadChromaOffset(reader, crWeight, chromaDenom);
                }
                weights[i] = new(lumaWeight, lumaOffset, cbWeight, cbOffset, crWeight, crOffset);
            }
            return weights;
        }

        private static int ReadChromaOffset(BitReader reader, int weight, int denominator)
        {
            int delta = HevcSyntax.Se(reader, -512, 511, "delta_chroma_offset");
            return Math.Clamp(128 + delta - ((128 * weight) >> denominator), -128, 127);
        }
    }
}
