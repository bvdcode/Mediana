// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal enum Av1RestorationType : byte
    {
        None,
        Wiener,
        SelfGuided,
        Switchable
    }
}
