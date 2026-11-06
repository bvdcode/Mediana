// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct SilkPitchIndices(short LagIndex, byte ContourIndex, byte PeriodicityIndex, byte ScaleIndex);
}
