// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1CompoundPrediction(Av1CompoundWeights Weights,
        Av1CompoundMaskType Mask = Av1CompoundMaskType.None, int Index = 0, int Sign = 0);
}
