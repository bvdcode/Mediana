// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Svg
{
    internal class PngHeader
    {
        internal int Width, Height, Depth, Color, Interlace;
        internal byte[] Palette = Array.Empty<byte>(), Transparency = Array.Empty<byte>();
    }

}
