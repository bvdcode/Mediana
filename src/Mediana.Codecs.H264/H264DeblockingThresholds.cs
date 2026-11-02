// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264DeblockingThresholds(int Alpha, int Beta, int First, int Second, int Third)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short Clipping(int strength) => strength switch
        {
            0 or 4 => 0,
            1 => (short)First,
            2 => (short)Second,
            3 => (short)Third,
            _ => throw new InvalidOperationException("Invalid H.264 boundary strength.")
        };
    }
}
