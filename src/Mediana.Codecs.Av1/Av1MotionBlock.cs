// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1MotionBlock
    {
        private byte references;
        private byte mode;
        internal byte Width;
        internal byte Height;
        private short row;
        private short column;
        internal short SecondRow;
        internal short SecondColumn;
        internal byte FilterY;
        private byte filterX;
        internal byte Transform;
        internal byte TransformSize;

        internal byte FilterX { readonly get => (byte)(filterX & 3); set => filterX = (byte)((filterX & ~3) | value); }
        internal bool InterIntra { readonly get => (filterX & 4) != 0; set => filterX = (byte)((filterX & ~4) | (value ? 4 : 0)); }
        internal byte Reference { readonly get => (byte)(references & 7); set => references = (byte)((references & ~7) | value); }
        internal byte SecondReference { readonly get => (byte)((references >> 3) & 7); set => references = (byte)((references & ~(7 << 3)) | (value << 3)); }
        internal bool SkipMode { readonly get => (references & 64) != 0; set => references = (byte)((references & ~64) | (value ? 64 : 0)); }
        internal int CompoundGroup { readonly get => references >> 7; set => references = (byte)((references & 127) | (value << 7)); }
        internal byte Mode { readonly get => (byte)(mode & 31); set => mode = (byte)((mode & ~31) | value); }
        internal int CompoundIndex { readonly get => (mode >> 5) & 1; set => mode = (byte)((mode & ~32) | (value << 5)); }
        internal int MotionMode { readonly get => mode >> 6; set => mode = (byte)((mode & 63) | (value << 6)); }
        internal int Row { readonly get => row; set => row = checked((short)value); }
        internal int Column { readonly get => column; set => column = checked((short)value); }
    }
}
