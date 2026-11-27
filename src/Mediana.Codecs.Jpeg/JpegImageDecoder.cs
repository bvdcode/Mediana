// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Decodes 8-bit sequential and progressive Huffman JPEG to independent RGBA32, or CMYK32 with an embedded CMYK ICC profile. Encoded input is borrowed;
    /// progressive coefficients, component samples and output are charged to the allocator. Chroma uses bilinear reconstruction at its declared sample positions.
    /// JFIF/Adobe/Exif declare component interpretation; Exif sRGB labels are retained and orientation applied.
    /// Predictive Huffman JPEG (SOF3) supports 2–16-bit input and presents reconstructed samples at 8 bits.
    /// Supports device CMYK and Adobe CMYK/YCCK. Arithmetic coding and custom Exif colorimetry are unsupported.</summary>
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
            if (sourceColorSpace is not (null or JpegColorSpace.Rgb or JpegColorSpace.YCbCr or
                JpegColorSpace.Cmyk or JpegColorSpace.InvertedCmyk or JpegColorSpace.Ycck))
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
            => ValueTask.FromResult(ReadInfo(encoded, cancellationToken));

        /// <summary>Reads image metadata without decoding component samples.</summary>
        public ImageInfo ReadInfo(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            using JpegDocument image = new(encoded, options, sourceColorSpace, sampleAspectRatio, cancellationToken);
            image.Read(null);
            return Describe(image);
        }

        /// <summary>Inspects the JPEG at offset zero of a containing field and reports its length through EOI.</summary>
        public ImageInfo ReadEmbeddedInfo(ReadOnlyMemory<byte> field, out int encodedLength, CancellationToken cancellationToken = default)
            => ReadEmbeddedInfo(field, out encodedLength, out _, true, cancellationToken);

        internal ImageInfo ReadEmbeddedInfo(ReadOnlyMemory<byte> field, out int encodedLength, out JpegColorSpace? componentConvention,
            bool inspectMetadata, CancellationToken cancellationToken)
        {
            using JpegDocument image = new(field, options, sourceColorSpace, sampleAspectRatio, cancellationToken);
            image.Read(null, embedded: true, inspectMetadata: inspectMetadata);
            componentConvention = image.Components switch
            {
                [{ Id: 1 }, { Id: 2 }, { Id: 3 }] => JpegColorSpace.YCbCr,
                [{ Id: (byte)'R' }, { Id: (byte)'G' }, { Id: (byte)'B' }] => JpegColorSpace.Rgb,
                _ => null
            };
            encodedLength = image.BytesConsumed;
            return Describe(image);
        }

        private static ImageInfo Describe(JpegDocument image)
        {
            bool swap = image.Metadata.Orientation.SwapsAxes;
            return new ImageInfo("jpeg", swap ? image.Height : image.Width, swap ? image.Width : image.Height,
                ImageDimensionUnit.Pixels, metadata: image.Metadata.Inspect(image));
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
                ContainerColorSpace = options.ContainerColorSpace,
                ContainerExifOrientation = options.ContainerExifOrientation,
                MaximumPixels = Math.Min(maximum, options.MaximumPixels),
                MaximumEncodedBytes = options.MaximumEncodedBytes,
                MaximumMarkers = options.MaximumMarkers,
                ChromaConflictPolicy = options.ChromaConflictPolicy,
                AllowUnalignedExifOffsets = options.AllowUnalignedExifOffsets
            } : options;
            using JpegDocument image = new(encoded, limits, sourceColorSpace, sampleAspectRatio, cancellationToken);
            image.Read(allocator, resolution: request);
            foreach (string warning in image.Warnings)
            {
                request.WarningHandler?.Invoke(warning);
            }
            return JpegPixelWriter.Create(image, allocator, cancellationToken);
        }
    }
}
