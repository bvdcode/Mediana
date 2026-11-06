// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Gif
{
    internal record GifFrame(int Left, int Top, int Width, int Height, bool Interlaced,
        int Delay, int Disposal, int TransparentIndex, ReadOnlyMemory<byte> Palette,
        int MinimumCodeSize, ReadOnlyMemory<byte> Blocks);
}
