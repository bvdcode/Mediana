// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    [Flags]
    internal enum H264SourceScanTypes
    {
        None = 0,
        Progressive = 1,
        Interlaced = 2,
        Unknown = 4
    }
}
