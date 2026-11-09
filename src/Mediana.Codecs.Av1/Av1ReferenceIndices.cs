// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    [InlineArray(Count)]
    internal struct Av1ReferenceIndices
    {
        internal const int Count = 7;
        private int first;
    }
}
