// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacPsHuffman
    {
        public static int Read(ref AacBitReader bits, AacPsCodebook book)
        {
            ReadOnlySpan<sbyte> table = book switch
            {
                AacPsCodebook.FineFrequency => FineFrequency,
                AacPsCodebook.FineTime => FineTime,
                AacPsCodebook.CoarseFrequency => CoarseFrequency,
                AacPsCodebook.CoarseTime => CoarseTime,
                AacPsCodebook.CoherenceFrequency => CoherenceFrequency,
                AacPsCodebook.CoherenceTime => CoherenceTime,
                AacPsCodebook.PhaseDifferenceFrequency => PhaseDifferenceFrequency,
                AacPsCodebook.PhaseDifferenceTime => PhaseDifferenceTime,
                AacPsCodebook.OverallPhaseFrequency => OverallPhaseFrequency,
                AacPsCodebook.OverallPhaseTime => OverallPhaseTime,
                _ => throw new ArgumentOutOfRangeException(nameof(book))
            };
            int node = 0;
            do
            {
                node = table[node * 2 + bits.Read(1)];
            }
            while (node >= 0);
            return node + 64;
        }

        private static ReadOnlySpan<sbyte> FineFrequency =>
        [
            1, -64, 52, 2, -63, 3, 51, 4, 50, 5, 49, 6, 48, 7, 44, 8,
            18, 9, 10, -71, -55, 11, 12, -74, 28, 13, -51, 14, 15, 43, 57, 16,
            17, 39, -94, -93, -56, 19, 20, -73, 34, 21, -52, 22, -50, 23, 24, -79,
            41, 25, 27, 26, -92, -91, -90, -89, 29, -77, -49, 30, 31, 54, 33, 32,
            -88, -87, -86, -42, 35, -76, 36, -78, 37, 42, 38, 40, -85, -43, -84, -44,
            -83, -45, -82, -46, -81, -47, -80, -48, -57, 45, 46, -72, -54, 47, -75, -53,
            -70, -58, -69, -59, -68, -60, -67, -61, 53, -65, -66, -62, 56, 55, -41, -40,
            -39, -38, 59, 58, -37, -36, -35, -34,
        ];

        private static ReadOnlySpan<sbyte> FineTime =>
        [
            1, -64, -63, 2, 3, -65, 4, 53, 51, 5, -61, 6, 49, 7, 8, 25,
            38, 9, -57, 10, 11, 47, 16, 12, 13, 22, 57, 14, 15, 21, -94, -93,
            -51, 17, 44, 18, 20, 19, -92, -91, -90, -38, -89, -39, 23, -79, 24, -81,
            -88, -40, 26, -69, 48, 27, 28, 33, 29, 45, 30, -76, -49, 31, -47, 32,
            -87, -41, 34, -73, 35, -75, 36, 54, 37, -80, -86, -42, 39, -71, -55, 40,
            -53, 41, 42, -77, 43, 55, -85, -84, -83, -82, -52, 46, -78, -50, -74, -54,
            -72, -56, -59, 50, -70, -58, 52, -67, -68, -60, -66, -62, -48, 56, -46, -45,
            -44, -43, 59, 58, -37, -36, -35, -34,
        ];

        private static ReadOnlySpan<sbyte> CoarseFrequency =>
        [
            -64, 1, 24, 2, 23, 3, 22, 4, 21, 5, -59, 6, -69, 7, -58, 8,
            -70, 9, -71, 10, -57, 11, 20, 12, 25, 13, 19, 14, 15, 17, -74, 16,
            -75, -78, 18, 26, -77, -76, -73, -53, -56, -72, -68, -60, -61, -67, -62, -66,
            -63, -65, -55, -54, -52, 27, -51, -50,
        ];

        private static ReadOnlySpan<sbyte> CoarseTime =>
        [
            -64, 1, -65, 2, -63, 3, -66, 4, -62, 5, -67, 6, -61, 7, -68, 8,
            -60, 9, -69, 10, -59, 11, -70, 12, -58, 13, -57, 14, -71, 15, 24, 16,
            17, 20, 18, 19, -55, -78, -77, -76, 21, 25, 22, 23, -75, -74, -73, -54,
            -56, -72, 26, 27, -53, -52, -51, -50,
        ];

        private static ReadOnlySpan<sbyte> CoherenceFrequency =>
        [
            -64, 1, -63, 2, -65, 3, -62, 4, -66, 5, -61, 6, -67, 7, -60, 8,
            -59, 9, -68, 10, -58, 11, -69, 12, -57, 13, -70, -71,
        ];

        private static ReadOnlySpan<sbyte> CoherenceTime =>
        [
            -64, 1, -63, 2, -65, 3, -62, 4, -66, 5, -61, 6, -67, 7, -60, 8,
            -68, 9, -59, 10, -69, 11, -58, 12, -70, 13, -71, -57,
        ];

        private static ReadOnlySpan<sbyte> PhaseDifferenceFrequency =>
        [
            1, -64, 2, 3, -63, 6, 5, 4, -62, -57, -61, -58, -60, -59,
        ];

        private static ReadOnlySpan<sbyte> PhaseDifferenceTime =>
        [
            1, -64, 3, 2, -63, -57, 5, 4, -62, -58, -59, 6, -60, -61,
        ];

        private static ReadOnlySpan<sbyte> OverallPhaseFrequency =>
        [
            1, -64, 2, 3, -57, -63, 5, 4, -62, 6, -61, -58, -59, -60,
        ];

        private static ReadOnlySpan<sbyte> OverallPhaseTime =>
        [
            1, -64, 3, 2, -63, -57, 4, 5, -59, -62, -58, 6, -60, -61,
        ];

    }
}
