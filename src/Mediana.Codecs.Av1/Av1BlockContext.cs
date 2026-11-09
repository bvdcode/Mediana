// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1BlockContext<T> where T : unmanaged
    {
        internal byte Mode;
        internal byte Level;
        internal byte Dc;
        internal byte WidthLog;
        internal byte HeightLog;
        private byte skip;
        internal byte Skip { readonly get => (byte)(skip & 1); set => skip = (byte)((skip & ~1) | value); }
        internal bool SegmentPredicted { readonly get => (skip & 2) != 0; set => skip = (byte)((skip & ~2) | (value ? 2 : 0)); }
        internal byte TransformWidth;
        internal byte TransformHeight;
        internal byte PaletteCount;
        internal Av1PaletteColors<T> Palette;
        internal byte UvPaletteCount;
        internal Av1PaletteColors<T> UvPalette;
    }
}
