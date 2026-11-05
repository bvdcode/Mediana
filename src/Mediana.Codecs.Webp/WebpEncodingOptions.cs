// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    public record WebpEncodingOptions
    {
        public WebpCompression Compression { get; init; } = WebpCompression.Lossless;
        public int Quality { get; init; } = 75;
        public WebpMetadata? Metadata { get; init; }

        internal void Validate()
        {
            switch (Compression)
            {
                case WebpCompression.Lossless:
                case WebpCompression.Lossy: break;
                default: throw new ArgumentOutOfRangeException(nameof(Compression));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(Quality);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Quality, 100);
        }
    }
}
