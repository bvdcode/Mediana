// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1InterIntraPrediction(bool Enabled, int Mode, int WedgeIndex = -1)
    {
        internal Av1IntraMode Intra => Mode switch
        {
            0 => default,
            1 => new(1, 0),
            2 => new(2, 0),
            3 => new(9, 0),
            _ => throw new InvalidDataException("The AV1 inter-intra prediction mode is invalid.")
        };
    }
}
