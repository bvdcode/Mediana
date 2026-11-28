// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal record H264PredictionWeight(int LumaWeight, int LumaOffset, int CbWeight, int CbOffset, int CrWeight, int CrOffset);
}
