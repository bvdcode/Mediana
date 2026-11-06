// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltFrameSyntax(CeltFrameFlags Flags, CeltAllocationControls Controls,
        CeltAllocationInfo Allocation);
}
