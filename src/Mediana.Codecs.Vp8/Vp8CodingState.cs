// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal class Vp8CodingState
    {
        internal readonly byte[] Coefficients = Vp8ProbabilityTables.Coefficients.ToArray();
        private static ReadOnlySpan<byte> DefaultLuma => [112, 86, 140, 37];
        private static ReadOnlySpan<byte> DefaultChroma => [162, 101, 204];
        private static ReadOnlySpan<byte> DefaultMotion =>
        [162, 128, 225, 146, 172, 147, 214, 39, 156, 128, 129, 132, 75, 145, 178, 206, 239, 254, 254,
         164, 128, 204, 170, 119, 235, 140, 230, 228, 128, 130, 130, 74, 148, 180, 203, 236, 254, 254];
        internal readonly byte[] Luma = DefaultLuma.ToArray();
        internal readonly byte[] Chroma = DefaultChroma.ToArray();
        internal readonly byte[] Motion = DefaultMotion.ToArray();
        internal readonly int[] SegmentQuantizers = new int[4], SegmentFilters = new int[4];
        internal readonly int[] ReferenceDeltas = new int[4], ModeDeltas = new int[4];
        internal bool AbsoluteSegments;

        internal void Reset()
        {
            Vp8ProbabilityTables.Coefficients.CopyTo(Coefficients);
            DefaultLuma.CopyTo(Luma); DefaultChroma.CopyTo(Chroma); DefaultMotion.CopyTo(Motion);
            Array.Clear(SegmentQuantizers); Array.Clear(SegmentFilters);
            Array.Clear(ReferenceDeltas); Array.Clear(ModeDeltas); AbsoluteSegments = false;
        }
    }
}
