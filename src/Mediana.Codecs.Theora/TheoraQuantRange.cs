// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal class TheoraQuantRange(int[] sizes, int[] matrices)
    {
        internal int[] Sizes { get; } = sizes;
        internal int[] Matrices { get; } = matrices;
    }
}
