// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Images.Heif.Container;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Images.Heif
{
    /// <summary>Registered HEIC primary-image decoding. HEIF transforms are applied by the document decoder;
    /// pixel aspect is an explicit caller interpretation, and ICC profiles are retained for common color processing.</summary>
    public class HeicImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly SampleAspectRatio sampleAspectRatio;
        private readonly HeicDecodeOptions options;

        public HeicImageDecoder(SampleAspectRatio sampleAspectRatio, HeicDecodeOptions? options = null)
        {
            sampleAspectRatio.Validate();
            this.sampleAspectRatio = sampleAspectRatio;
            this.options = options ?? HeicDecodeOptions.Default;
        }

        public string ImplementationId => "heif.managed.image";
        public int ProbeLength => 512;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;

        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) =>
            HeifFileType.ContainsBrand(prefix, [HeifBox.Code("heic"), HeifBox.Code("heix"), HeifBox.Code("hevc"),
                HeifBox.Code("hevx"), HeifBox.Code("mif1"), HeifBox.Code("msf1")]);

        /// <summary>Reads transformed primary-image dimensions without decoding pixels or applying the decode pixel limit.
        /// Encoded input, item-count and structural validation limits still apply.</summary>
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HeifContainer container = new HeifContainerReader(options).Read(encoded);
            ImageProperties properties = ImageProperties.Read(container, container.Items[container.PrimaryItemId]);
            (int width, int height) = properties.GetDisplaySize(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ImageInfo("heif", width, height, ImageDimensionUnit.Pixels,
                metadata: HeifMetadataReader.Read(container, properties, cancellationToken)));
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            return await HeicDocument.DecodePrimaryFrameAsync(encoded, options, sampleAspectRatio, allocator, cancellationToken).ConfigureAwait(false);
        }
    }
}
