// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal enum H264DisplayStructure
    {
        Frame,
        TopField,
        BottomField,
        TopBottom,
        BottomTop,
        TopBottomTop,
        BottomTopBottom,
        FrameDouble,
        FrameTriple
    }
}
