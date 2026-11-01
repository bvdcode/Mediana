// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct EbmlVint(ulong Value, int Width)
    {
        public bool IsUnknown => Value == (1UL << (7 * Width)) - 1;
    }
}
