// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1RestorationUnit
    {
        internal Av1RestorationType Type;
        internal byte Set;
        internal sbyte V0, V1, V2, H0, H1, H2, X0, X1;
        internal static Av1RestorationUnit Initial => new() { V0 = 3, V1 = -7, V2 = 15, H0 = 3, H1 = -7, H2 = 15, X0 = -32, X1 = 31 };
    }
}
