// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jbig2
{
    /// <summary>Registered standalone JBIG2 page decoding with an explicit pixel-aspect interpretation.</summary>
    public class Jbig2ImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly SampleAspectRatio sampleAspectRatio;
        private readonly Jbig2DecodeOptions options;

        public Jbig2ImageDecoder(SampleAspectRatio sampleAspectRatio, Jbig2DecodeOptions? options = null)
        {
            sampleAspectRatio.Validate();
            this.sampleAspectRatio = sampleAspectRatio;
            this.options = options ?? new Jbig2DecodeOptions();
        }

        public string ImplementationId => "jbig2.managed.image";
        public int ProbeLength => 8;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith<byte>([0x97, 0x4a, 0x42, 0x32, 13, 10, 0x1a, 10]);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            DecodeBudget budget = new(options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length > options.MaximumEncodedBytes)
            {
                throw new InvalidDataException("JBIG2 exceeds MaximumEncodedBytes.");
            }
            return ValueTask.FromResult(new DocumentDecoder(budget).ReadInfo(encoded));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            Jbig2Document document = Jbig2Decoder.Decode(encoded, options: options, cancellationToken: cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, document.Pages.Count, nameof(request.PageIndex));
            return ValueTask.FromResult(document.Pages[request.PageIndex].ToVideoFrame(allocator, sampleAspectRatio, cancellationToken));
        }
    }
}
