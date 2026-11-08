// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1BlockContext
    {
        internal byte WidthLog;
        internal byte HeightLog;
        internal byte Skip;
        internal byte PaletteCount;
        internal Av1PaletteColors Palette;
    }
}
