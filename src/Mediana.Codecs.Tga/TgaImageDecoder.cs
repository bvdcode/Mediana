// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Tga
{
    /// <summary>Decodes true-color, grayscale and indexed TGA, including RLE, origin transforms and legacy interleaving.</summary>
    public class TgaImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "tga.managed.image";
        public int ProbeLength => 18;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => TgaImage.Recognize(prefix);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TgaImage image = new(encoded);
            return ValueTask.FromResult(new ImageInfo("tga", image.Width, image.Height, ImageDimensionUnit.Pixels, metadata: image.Metadata));
        }
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            TgaImage image = new(encoded);
            if (image.HasColorCorrection) { throw new NotSupportedException("TGA color-correction tables are not supported."); }
            if (request.MaximumPixels is int limit && (long)image.Width * image.Height > limit) { throw new InvalidDataException("TGA source exceeds MaximumPixels."); }
            TransferFunction transfer = TransferFunction.Unspecified;
            if (image.GammaNumerator != 0 && image.GammaDenominator != 0)
            {
                if (image.GammaNumerator == image.GammaDenominator) { transfer = TransferFunction.Linear; }
                else if (image.GammaNumerator * 5 == image.GammaDenominator * 11) { transfer = TransferFunction.Gamma22; }
                else if (image.GammaNumerator * 5 == image.GammaDenominator * 14) { transfer = TransferFunction.Gamma28; }
                else { throw new NotSupportedException("The declared TGA gamma is not supported."); }
            }
            VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32,
                new(Transfer: transfer, Matrix: MatrixCoefficients.Rgb, Range: ColorRange.Full), image.Aspect);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            TgaPixelReader.Decode(image, writable.Span, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer pixels = writable.Publish();
            return new(format, pixels, layout);
        }
    }
}
