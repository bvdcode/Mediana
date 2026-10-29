// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264MemoryOperation(int Operation, int DifferenceMinusOne = 0, int LongTermPictureNumber = 0,
        int LongTermFrameIndex = 0, int MaxLongTermFrameIndexPlusOne = 0);
}
