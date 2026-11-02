// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Limits checked before JPEG sample allocation and while reading marker segments.</summary>
    public class JpegDecodeOptions
    {
        public int MaximumEncodedBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumPixels { get; init; } = 16 * 1024 * 1024;
        public int MaximumMarkers { get; init; } = 100_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEncodedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, int.MaxValue / 4);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMarkers);
        }
    }
}
