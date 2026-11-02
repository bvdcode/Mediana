// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Png
{
    /// <summary>Resource limits checked before allocating PNG pixels or inflating image data.</summary>
    public class PngDecodeOptions
    {
        public int MaximumEncodedBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumPixels { get; init; } = 16 * 1024 * 1024;
        public int MaximumChunks { get; init; } = 100_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEncodedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, int.MaxValue / 4);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumChunks);
        }
    }
}
