// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264PreparedMacroblock(int intraMode, int pattern, ReadOnlySpan<int> coefficients,
        ReadOnlySpan<byte> pixels, long distortion)
    {
        public int IntraMode { get; } = intraMode;
        public int Pattern { get; } = pattern;
        public ReadOnlySpan<int> Coefficients { get; } = coefficients;
        public ReadOnlySpan<byte> Pixels { get; } = pixels;
        public long Distortion { get; } = distortion;
        public bool IsIntra => IntraMode >= 0;
    }
}
