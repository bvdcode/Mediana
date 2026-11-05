// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Decodes 8-bit sequential and progressive Huffman JPEG to independent RGBA32. Encoded input is borrowed;
    /// progressive coefficients, component samples and output are charged to the allocator. Chroma uses centered bilinear reconstruction.
    /// JFIF/Adobe/Exif declare component interpretation; Exif sRGB labels are retained and orientation applied.
    /// Lossless/arithmetic, custom Exif colorimetry and CMYK/YCCK are currently unsupported.</summary>
    public class JpegImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        private readonly JpegDecodeOptions options;
        private readonly JpegColorSpace? sourceColorSpace;
        private readonly SampleAspectRatio? sampleAspectRatio;

        public JpegImageDecoder(JpegDecodeOptions? options = null, JpegColorSpace? sourceColorSpace = null,
            SampleAspectRatio? sampleAspectRatio = null)
        {
            this.options = options ?? new();
            this.options.Validate();
            if (sourceColorSpace is not (null or JpegColorSpace.Rgb or JpegColorSpace.YCbCr))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceColorSpace));
            }
            sampleAspectRatio?.Validate();
            this.sourceColorSpace = sourceColorSpace;
            this.sampleAspectRatio = sampleAspectRatio;
        }

        public string ImplementationId => "jpeg.managed.image";
        public int ProbeLength => 3;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 3 && prefix[0] == 255 && prefix[1] == 216 && prefix[2] == 255;

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            using JpegDocument image = new(encoded, options, sourceColorSpace, sampleAspectRatio, cancellationToken);
            image.Read(null);
            bool swap = image.Metadata.Orientation.SwapsAxes;
            return ValueTask.FromResult(new ImageInfo("jpeg", swap ? image.Height : image.Width, swap ? image.Width : image.Height,
                ImageDimensionUnit.Pixels, metadata: image.Metadata.Inspect(image)));
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
            JpegDecodeOptions limits = request.MaximumPixels is int maximum ? new()
            {
                MaximumPixels = Math.Min(maximum, options.MaximumPixels),
                MaximumEncodedBytes = options.MaximumEncodedBytes,
                MaximumMarkers = options.MaximumMarkers
            } : options;
            using JpegDocument image = new(encoded, limits, sourceColorSpace, sampleAspectRatio, cancellationToken);
            image.Read(allocator);
            foreach (string warning in image.Warnings)
            {
                request.WarningHandler?.Invoke(warning);
            }
            return JpegPixelWriter.Create(image, allocator, cancellationToken);
        }
    }
}
