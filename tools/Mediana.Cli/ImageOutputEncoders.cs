// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Codecs.Webp;

namespace Mediana.Cli
{
    internal static class ImageOutputEncoders
    {
        private static readonly IReadOnlyDictionary<string, Func<ImageOutputSettings, IImageEncoder>> Encoders = new Dictionary<string, Func<ImageOutputSettings, IImageEncoder>>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = Png,
            [".webp"] = settings => new WebpImageEncoder(Webp(settings))
        };

        private static readonly IReadOnlyDictionary<string, Func<ImageOutputSettings, IImageSequenceEncoder>> Sequences =
            new Dictionary<string, Func<ImageOutputSettings, IImageSequenceEncoder>>(StringComparer.OrdinalIgnoreCase)
            {
                [".webp"] = settings => new WebpImageSequenceEncoder(Webp(settings))
            };

        internal static IImageEncoder ForPath(string path, ImageOutputSettings? settings = null) =>
            Encoders.TryGetValue(Path.GetExtension(path), out Func<ImageOutputSettings, IImageEncoder>? create)
                ? create(settings ?? new()) : throw new NotSupportedException("Image output requires a registered extension: .png or .webp.");

        internal static IImageSequenceEncoder SequenceForPath(string path, ImageOutputSettings settings) =>
            Sequences.TryGetValue(Path.GetExtension(path), out Func<ImageOutputSettings, IImageSequenceEncoder>? create)
                ? create(settings) : throw new NotSupportedException("Animated image output requires a registered sequence encoder: .webp.");

        private static IImageEncoder Png(ImageOutputSettings settings)
        {
            if (settings.Compression is not (null or ImageCompressionMode.Lossless) || settings.Quality is not null)
            {
                throw new NotSupportedException("PNG uses lossless compression and does not accept a lossy quality setting.");
            }
            return new PngImageEncoder();
        }

        private static WebpEncodingOptions Webp(ImageOutputSettings settings)
        {
            if (settings.Quality is < 0 or > 100) { throw new ArgumentOutOfRangeException(nameof(settings), "Image quality must be between 0 and 100."); }
            WebpCompression compression = settings.Compression switch
            {
                null or ImageCompressionMode.Lossless => WebpCompression.Lossless,
                ImageCompressionMode.Lossy => WebpCompression.Lossy,
                _ => throw new ArgumentOutOfRangeException(nameof(settings))
            };
            if (compression == WebpCompression.Lossless && settings.Quality is not null)
            {
                throw new ArgumentException("Image quality controls lossy compression; select --image-compression Lossy.", nameof(settings));
            }
            return new() { Compression = compression, Quality = settings.Quality ?? 75 };
        }
    }
}
