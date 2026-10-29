// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264BPartition(int X, int Y, int Width, int Height, H264BPredictionMode Mode);
}
