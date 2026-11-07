// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcPredictionReference(DecodedPicture Picture, HevcMotionVector Motion, int Index);
}
