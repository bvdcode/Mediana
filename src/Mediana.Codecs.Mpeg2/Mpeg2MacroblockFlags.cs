// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    [Flags]
    internal enum Mpeg2MacroblockFlags
    {
        Quantizer = 1,
        Forward = 2,
        Backward = 4,
        Pattern = 8,
        Intra = 16
    }
}
