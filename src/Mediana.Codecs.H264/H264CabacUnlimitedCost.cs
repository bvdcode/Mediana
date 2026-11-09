// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CabacUnlimitedCost<TWriter> : IH264CabacCostLimit<TWriter>
        where TWriter : struct, IH264CabacBinWriter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Reached(TWriter writer) => false;
    }
}
