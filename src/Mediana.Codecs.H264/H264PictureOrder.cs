// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264PictureOrder(int Top, int Bottom)
    {
        public int Value => Math.Min(Top, Bottom);
    }
}
