// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>JPEG resource limits and explicit metadata conflict handling.</summary>
    public class JpegDecodeOptions
    {
        public int MaximumEncodedBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumPixels { get; init; } = 16 * 1024 * 1024;
        public int MaximumMarkers { get; init; } = 100_000;
        public JpegChromaConflictPolicy ChromaConflictPolicy { get; init; }
        /// <summary>Allows byte-aligned Exif directories and rational values that violate TIFF word alignment.
        /// Bounds, tag types, counts and values remain validated. Decoding reports a warning through ImageDecodeRequest.</summary>
        public bool AllowUnalignedExifOffsets { get; init; }

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumEncodedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumPixels, int.MaxValue / 4);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMarkers);
            switch (ChromaConflictPolicy)
            {
                case JpegChromaConflictPolicy.Reject:
                case JpegChromaConflictPolicy.UseJfif:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ChromaConflictPolicy));
            }
        }
    }
}
