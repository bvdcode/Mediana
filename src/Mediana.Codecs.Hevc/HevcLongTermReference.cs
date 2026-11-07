// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcLongTermReference(int PictureOrderCountLsb, bool UsedByCurrentPicture,
        bool HasMsbCycle = false, long MsbCycle = 0);
}
