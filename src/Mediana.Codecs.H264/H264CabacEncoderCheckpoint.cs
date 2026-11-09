// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264CabacEncoderCheckpoint(H264BitWriterCheckpoint Writer, int Range, int Low,
        long Outstanding, ulong OutputBits, int OutputCount, bool FirstBit, bool Terminated, long BinCount);
}
