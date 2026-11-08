// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal interface IH264MacroblockAnalysisStrategy
    {
        static abstract bool Enabled { get; }
        static abstract ReadOnlySpan<int> SubpixelOffsetsX { get; }
        static abstract ReadOnlySpan<int> SubpixelOffsetsY { get; }
    }
}
