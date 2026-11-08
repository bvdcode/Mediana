// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1IntraMode(int Mode, int AngleDelta, int? Filter = null)
    {
        internal int TransformMode => Filter switch
        {
            null => Mode,
            0 or 4 => 0,
            1 => 1,
            2 => 2,
            3 => 6,
            _ => throw new InvalidDataException("The AV1 recursive intra filter mode is invalid.")
        };
    }
}
