// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264DetailedMacroblockAnalysis : IH264MacroblockAnalysisStrategy
    {
        public static bool Enabled => true;
    }
}
