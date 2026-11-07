// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcPredictionWeight(int LumaWeight, int LumaOffset,
        int CbWeight, int CbOffset, int CrWeight, int CrOffset);
}
