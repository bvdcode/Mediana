// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltAllocationInfo(int CodedBands, int IntensityBand,
        bool DualStereo, int BalanceEighthBits);
}
