// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4MovieFragment(long Offset, ReadOnlyMemory<byte> Payload);
}
