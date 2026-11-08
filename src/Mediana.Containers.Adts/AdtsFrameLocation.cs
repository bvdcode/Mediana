// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Adts
{
    internal readonly record struct AdtsFrameLocation(long Offset, int ByteLength, bool HasCrc);
}
