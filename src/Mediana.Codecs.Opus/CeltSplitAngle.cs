// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltSplitAngle(int Angle, int MidGain, int SideGain, int AllocationDelta,
        int EighthBitsUsed, bool Inverted, uint Fill);
}
