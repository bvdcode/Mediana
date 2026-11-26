// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>JPEG resource limits and explicit metadata conflict handling.</summary>
    public class JpegDecodeOptions
    {
        /// <summary>Container-declared RGB or YCbCr for a three-component JPEG with no color declaration.</summary>
        public JpegColorSpace? ContainerColorSpace { get; init; }
        /// <summary>TIFF orientation of the containing image, used only when the JPEG has no orientation tag.</summary>
        public int ContainerExifOrientation { get; init; } = 1;
        public int MaximumEncodedBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumPixels { get; init; } = 16 * 1024 * 1024;
        public int MaximumMarkers { get; init; } = 100_000;
        public JpegChromaConflictPolicy ChromaConflictPolicy { get; init; }
        /// <summary>Allows byte-aligned Exif directories and rational values that violate TIFF word alignment.
        /// Bounds, tag types, counts and values remain validated. Decoding reports a warning through ImageDecodeRequest.</summary>
        public bool AllowUnalignedExifOffsets { get; init; }

        internal void Validate()
        {
            if (ContainerColorSpace is not (null or JpegColorSpace.Rgb or JpegColorSpace.YCbCr)) { throw new ArgumentOutOfRangeException(nameof(ContainerColorSpace)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(ContainerExifOrientation, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ContainerExifOrientation, 8);
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
