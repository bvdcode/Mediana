// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal struct Vp8Macroblock
    {
        internal int Reference, Mode;
        internal Vp8MotionVector Motion;
        internal Vp8BlockVectors Blocks;
    }
}
