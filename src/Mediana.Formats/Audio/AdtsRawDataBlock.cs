// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>A protected raw-data-block range within an ADTS frame, excluding its trailing CRC word.</summary>
    public readonly record struct AdtsRawDataBlock(int Offset, int ByteLength, ushort Crc);
}
