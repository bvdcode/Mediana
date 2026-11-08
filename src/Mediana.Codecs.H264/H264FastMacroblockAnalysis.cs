// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264FastMacroblockAnalysis : IH264MacroblockAnalysisStrategy
    {
        public static bool Enabled => false;
        public static ReadOnlySpan<int> SubpixelOffsetsX => [-1, 0, 1, -1, 1, -1, 0, 1];
        public static ReadOnlySpan<int> SubpixelOffsetsY => [-1, -1, -1, 0, 0, 1, 1, 1];
    }
}
