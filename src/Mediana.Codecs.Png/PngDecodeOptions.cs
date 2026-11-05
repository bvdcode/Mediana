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
        /// <summary>Retains every Nth sample on each axis while still validating every source scanline.
        /// One decodes at full resolution. Higher values use nearest-neighbor decimation and one RGBA scanline of scratch storage.</summary>
        public int ReductionFactor { get; init; } = 1;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEncodedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, int.MaxValue / 4);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumChunks);
            ArgumentOutOfRangeException.ThrowIfLessThan(ReductionFactor, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ReductionFactor, 256);
        }
    }
}
