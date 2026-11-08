// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CabacUnlimitedCost : IH264CabacCostLimit<H264CabacOutputWriter>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Reached(H264CabacOutputWriter writer) => false;
    }
}
