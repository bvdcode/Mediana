// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264PictureOrder(int Top, int Bottom,
        H264PictureStructure Structure = H264PictureStructure.Frame)
    {
        public int Value => Structure switch
        {
            H264PictureStructure.Frame => Math.Min(Top, Bottom),
            H264PictureStructure.TopField => Top,
            H264PictureStructure.BottomField => Bottom,
            _ => throw new InvalidOperationException("Invalid H.264 picture structure.")
        };
    }
}
