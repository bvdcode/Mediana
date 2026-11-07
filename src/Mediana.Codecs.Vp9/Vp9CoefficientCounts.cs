// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9CoefficientCounts
    {
        internal readonly uint[] Tokens = new uint[1728], More = new uint[1152];
        internal void Clear() { Array.Clear(Tokens); Array.Clear(More); }
    }
}
