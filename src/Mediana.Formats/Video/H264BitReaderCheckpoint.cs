// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal readonly record struct H264BitReaderCheckpoint(int Offset, int BufferedBits, ulong Buffer,
        int ZeroBytes, int PreventionBytes);
}
