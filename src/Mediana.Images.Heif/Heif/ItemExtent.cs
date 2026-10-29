// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Container
{
    internal readonly struct ItemExtent(ulong offset, ulong length)
    {
        public ulong Offset { get; } = offset;
        public ulong Length { get; } = length;
    }
}
