// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    [InlineArray(8)]
    internal struct Av1PaletteColors<T> where T : unmanaged
    {
        private T value;
    }
}
