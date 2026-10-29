// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif
{
    /// <summary>
    /// Defines immutable resource limits for input files, image dimensions and item relationships.
    /// </summary>
    public class HeicDecodeOptions
    {
        /// <summary>
        /// Gets the default decoding limits.
        /// </summary>
        public static HeicDecodeOptions Default { get; } = new HeicDecodeOptions();

        /// <summary>
        /// Initializes a set of decoding limits.
        /// </summary>
        /// <param name="maxInputBytes">The maximum encoded input size in bytes; at least 32.</param>
        /// <param name="maxPixels">The maximum coded or visible pixels per image, at most int.MaxValue / 4.</param>
        /// <param name="maxItems">The maximum number of HEIF items in one document.</param>
        /// <param name="maxDerivationDepth">The maximum nesting depth of derived images.</param>
        /// <exception cref="ArgumentOutOfRangeException">A resource limit is invalid.</exception>
        public HeicDecodeOptions(int maxInputBytes = 64 * 1024 * 1024, int maxPixels = int.MaxValue / 4,
            int maxItems = 1024, int maxDerivationDepth = 16)
        {
            if (maxInputBytes < 32)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInputBytes), maxInputBytes, "The encoded input limit must be at least 32 bytes.");
            }

            if ((maxPixels <= 0) || (maxPixels > (int.MaxValue / 4)))
            {
                throw new ArgumentOutOfRangeException(nameof(maxPixels), maxPixels, "The pixel limit must be positive and fit in a managed RGBA8 buffer.");
            }

            if (maxItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "The document item limit must be positive.");
            }

            if (maxDerivationDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDerivationDepth), maxDerivationDepth, "The image derivation depth limit must be positive.");
            }

            MaxInputBytes = maxInputBytes;
            MaxPixels = maxPixels;
            MaxItems = maxItems;
            MaxDerivationDepth = maxDerivationDepth;
        }

        /// <summary>
        /// Gets the maximum encoded input size in bytes.
        /// </summary>
        public int MaxInputBytes { get; }

        /// <summary>
        /// Gets the maximum number of coded or visible pixels in one image.
        /// </summary>
        public int MaxPixels { get; }

        /// <summary>
        /// Gets the maximum number of HEIF items in one document.
        /// </summary>
        public int MaxItems { get; }

        /// <summary>
        /// Gets the maximum nesting depth of derived images.
        /// </summary>
        public int MaxDerivationDepth { get; }
    }
}
