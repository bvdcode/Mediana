// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct SilkFrameIndices(SilkSignalType Signal, byte QuantizationOffset,
        byte InterpolationQ2, byte Seed, SilkPitchIndices Pitch);
}
