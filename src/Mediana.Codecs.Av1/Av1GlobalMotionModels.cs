// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    [InlineArray(Av1ReferenceIndices.Count)]
    internal struct Av1GlobalMotionModels
    {
        private Av1GlobalMotionModel element;
    }
}
