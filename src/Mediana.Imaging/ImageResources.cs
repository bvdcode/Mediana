// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Bmp;
using Mediana.Codecs.Gif;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Webp;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Imaging
{
    /// <summary>Common embedded-image decoding and sRGB preparation. Does not load external files or URLs.</summary>
    public static class ImageResources
    {
        /// <summary>Managed raster defaults (PNG, JPEG, WebP, GIF, TIFF, BMP and ICO). A supplied context replaces this registry, including for nested images.</summary>
        public static ImageDecodeContext DefaultDecoders { get; } = new([
            new BmpImageDecoder(), new IcoImageDecoder(), new GifImageDecoder(), new TiffImageDecoder(), new WebpImageDecoder(),
            new PngImageDecoder(new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }),
            new JpegImageDecoder(new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength })]);

        public static async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeContext decoders,
            ImageDecodeRequest request, MediaBufferAllocator allocator, Func<VideoFormat, VideoColorInfo>? colorResolver = null,
            CancellationToken cancellationToken = default)
        {
            using VideoFrame frame = await decoders.DecodeAsync(encoded, request, allocator, cancellationToken).ConfigureAwait(false);
            return Prepare(frame, request, allocator, colorResolver, cancellationToken);
        }

        public static VideoFrame DecodeSynchronously(ReadOnlyMemory<byte> encoded, ImageDecodeContext decoders,
            ImageDecodeRequest request, MediaBufferAllocator allocator, Func<VideoFormat, VideoColorInfo>? colorResolver = null,
            CancellationToken cancellationToken = default)
        {
            using VideoFrame frame = decoders.Decode(encoded, request, allocator, cancellationToken);
            return Prepare(frame, request, allocator, colorResolver, cancellationToken);
        }

        private static VideoFrame Prepare(VideoFrame frame, ImageDecodeRequest request, MediaBufferAllocator allocator,
            Func<VideoFormat, VideoColorInfo>? colorResolver, CancellationToken token)
        {
            VideoColorInfo declared = frame.Format.GetColorInterpretation();
            VideoColorInfo color = colorResolver?.Invoke(frame.Format) ?? declared;
            if (declared != color) { request.WarningHandler?.Invoke("An embedded image uses the configured interpretation for missing color labels."); }
            return SrgbConverter.Apply(frame, color, allocator, token);
        }
    }
}
