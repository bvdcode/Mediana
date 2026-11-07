// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal enum HevcPartitionMode
    {
        Whole,
        Horizontal,
        Vertical,
        Quarters,
        NarrowTop,
        NarrowBottom,
        NarrowLeft,
        NarrowRight
    }
}
