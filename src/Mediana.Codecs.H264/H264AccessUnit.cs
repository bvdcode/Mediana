// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264AccessUnit(H264Configuration Configuration, IReadOnlyList<ReadOnlyMemory<byte>> Slices,
        bool EndSequence, bool EndStream);
}
