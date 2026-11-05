// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg2000
{
    /// <summary>Registered JP2/J2K decoding with an explicit pixel-aspect interpretation.</summary>
    public class Jpeg2000ImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly SampleAspectRatio sampleAspectRatio;
        private readonly Jpeg2000DecodeOptions options;
        private readonly Jpeg2000ColorSpace? sourceColorSpace;

        public Jpeg2000ImageDecoder(SampleAspectRatio sampleAspectRatio,
            Jpeg2000DecodeOptions? options = null, Jpeg2000ColorSpace? sourceColorSpace = null)
        {
            sampleAspectRatio.Validate();
            this.sampleAspectRatio = sampleAspectRatio;
            this.options = options ?? new Jpeg2000DecodeOptions();
            this.sourceColorSpace = sourceColorSpace;
        }

        public string ImplementationId => "jpeg2000.managed.image";
        public int ProbeLength => 12;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) =>
            prefix.StartsWith<byte>([0, 0, 0, 12, 0x6a, 0x50, 0x20, 0x20, 13, 10, 0x87, 10])
            || prefix.StartsWith<byte>([0xff, 0x4f, 0xff, 0x51]);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            DecodeBudget budget = new(options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length > options.MaximumEncodedBytes)
            {
                throw new InvalidDataException("JPEG 2000 exceeds MaximumEncodedBytes.");
            }
            (ReadOnlyMemory<byte> Stream, ContainerInfo Info) container = Jp2Container.Read(encoded);
            CodestreamSize size = CodestreamSize.Read(new ByteReader(container.Stream), container.Info, budget);
            MediaMetadata metadata = new("image", [new("media", "codec", "jpeg2000"),
                new("jpeg2000", "tile_dimensions", size.TileWidth, size.TileHeight), new("jpeg2000", "tile_count", size.TileCount),
                new("jpeg2000", "color_space", container.Info.ColorSpace.ToString()), new("jpeg2000", "alpha_component", container.Info.Alpha),
                new("jpeg2000", "has_icc_profile", container.Info.Profile)],
                size.Components.Select((component, index) => new MediaMetadata("component/" + index,
                    [new("media", "bit_depth", component.BitDepth), new("media", "signed", component.IsSigned),
                    new("media", "sample_spacing", component.HorizontalSampling, component.VerticalSampling)])));
            return ValueTask.FromResult(new ImageInfo("jpeg2000", size.X1 - size.X0, size.Y1 - size.Y0, ImageDimensionUnit.Pixels, metadata: metadata));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            Jpeg2000Image image = Jpeg2000Decoder.Decode(encoded, options, cancellationToken);
            return ValueTask.FromResult(image.ToVideoFrame(allocator, sampleAspectRatio, sourceColorSpace, cancellationToken));
        }
    }
}
