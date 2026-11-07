// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Vp9
{
    [InlineArray(8)]
    internal struct Vp9MotionSet
    {
        private Vp9MotionVector first;
    }
}
