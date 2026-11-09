// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264DetailedMacroblockAnalysis : IH264MacroblockAnalysisStrategy
    {
        public static bool Enabled => true;
        public static ReadOnlySpan<int> SubpixelOffsetsX => [0, -1, 1, 0];
        public static ReadOnlySpan<int> SubpixelOffsetsY => [-1, 0, 0, 1];
    }
}
