// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Vp8
{
    [InlineArray(16)]
    internal struct Vp8BlockVectors
    {
        private Vp8MotionVector element;
    }
}
