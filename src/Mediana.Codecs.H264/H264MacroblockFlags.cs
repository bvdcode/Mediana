// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    [Flags]
    internal enum H264MacroblockFlags : byte
    {
        None = 0,
        Transform8x8 = 1,
        StationarySkip = 2
    }
}
