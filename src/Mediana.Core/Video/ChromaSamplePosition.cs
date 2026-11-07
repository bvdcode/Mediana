// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    public static class ChromaSamplePosition
    {
        /// <summary>Returns sample offsets from the top-left luma sample in half-luma units.</summary>
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
