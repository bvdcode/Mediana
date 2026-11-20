// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs.Jpeg
{
    public record JpegEncodingOptions
    {
        /// <summary>Quantization quality from 0 to 100. Even 100 uses lossy DCT encoding.</summary>
        public int Quality { get; init; } = 75;
        public VideoChromaSampling ChromaSampling { get; init; } = VideoChromaSampling.Sampling420;
        /// <summary>Optional background for straight alpha, composited in encoded sRGB. Without it, nonopaque pixels are rejected.</summary>
        public JpegBackgroundColor? BackgroundColor { get; init; }

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(Quality);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Quality, 100);
            if (ChromaSampling is not (VideoChromaSampling.Monochrome or VideoChromaSampling.Sampling420
                or VideoChromaSampling.Sampling422 or VideoChromaSampling.Sampling444))
            {
                throw new ArgumentOutOfRangeException(nameof(ChromaSampling));
            }
        }
    }
}
