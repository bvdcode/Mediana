// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1GrainChannel(ReadOnlyMemory<Av1GrainPoint> Points,
        ReadOnlyMemory<sbyte> Coefficients, int Multiplier, int LumaMultiplier, int Offset);
}
