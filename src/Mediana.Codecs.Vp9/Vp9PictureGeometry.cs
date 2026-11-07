// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal readonly record struct Vp9PictureGeometry(int Width, int Height, int RenderWidth, int RenderHeight,
        int Profile, int BitDepth, int ColorSpace, bool FullRange, bool SubsamplingX, bool SubsamplingY);
}
