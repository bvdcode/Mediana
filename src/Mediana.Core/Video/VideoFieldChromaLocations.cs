// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Independent top/bottom field chroma locations, expressed on the full-frame luma grid.</summary>
    public readonly record struct VideoFieldChromaLocations(ChromaLocation Top, ChromaLocation Bottom)
    {
        public ChromaLocation GetLocation(VideoFieldParity parity) => parity switch
        {
            VideoFieldParity.Top => Top,
            VideoFieldParity.Bottom => Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(parity))
        };
    }
}
