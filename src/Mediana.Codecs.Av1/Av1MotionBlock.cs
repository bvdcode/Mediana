// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1MotionBlock
    {
        internal byte Reference;
        internal byte Mode;
        internal byte Width;
        internal byte Height;
        internal int Row;
        internal int Column;
        internal byte FilterY;
        internal byte FilterX;
        internal byte Transform;
    }
}
