// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Container
{
    internal readonly struct PropertyAssociation(int index, bool essential)
    {
        public int Index { get; } = index;
        public bool Essential { get; } = essential;
    }
}
