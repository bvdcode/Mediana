// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcPredictionBlock(int CodingX, int CodingY, int CodingSize,
        int X, int Y, int Width, int Height, int Index, HevcPartitionMode Mode);
}
