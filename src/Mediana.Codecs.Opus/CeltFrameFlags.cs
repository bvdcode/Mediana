// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltFrameFlags(bool IsSilent, bool IsTransient, bool IntraEnergy,
        CeltPostFilterParameters PostFilter);
}
