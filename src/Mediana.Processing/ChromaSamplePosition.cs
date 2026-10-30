// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class ChromaSamplePosition
    {
        // Rec. ITU-T H.264, Figure E-1; offsets in half-luma-sample units.
        public static (int Horizontal, int Vertical) InHalfLumaSamples(ChromaLocation location) => location switch
        {
            ChromaLocation.Left => (0, 1),
            ChromaLocation.Center => (1, 1),
            ChromaLocation.TopLeft => (0, 0),
            ChromaLocation.Top => (1, 0),
            ChromaLocation.BottomLeft => (0, 2),
            ChromaLocation.Bottom => (1, 2),
            ChromaLocation.Unspecified => throw new NotSupportedException("YCbCr processing requires an explicit chroma location."),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
    }
}
