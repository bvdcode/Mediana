// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltAllocationControls(CeltSpreading Spreading, int Trim,
        int ShapeBudgetEighthBits, int AntiCollapseEighthBits);
}
