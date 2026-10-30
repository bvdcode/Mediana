// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg2000
{
    /// <summary>Registered JP2/J2K decoding with an explicit pixel-aspect interpretation.</summary>
    public class Jpeg2000ImageDecoder : IImageDecoder
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
        public bool CanDecode(ReadOnlySpan<byte> prefix) =>
            prefix.StartsWith<byte>([0, 0, 0, 12, 0x6a, 0x50, 0x20, 0x20, 13, 10, 0x87, 10])
            || prefix.StartsWith<byte>([0xff, 0x4f, 0xff, 0x51]);

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
