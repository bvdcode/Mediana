// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltSplitMode(int Band, int FrameShift, int Dimensions, int Blocks,
        int OriginalBlocks, bool Stereo, int IntensityBand, bool DisableInversion = false);
}
