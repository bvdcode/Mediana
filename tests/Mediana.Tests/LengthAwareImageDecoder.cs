// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class LengthAwareImageDecoder(int length) : IImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "length.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        internal int DecodeCalls { get; private set; }

        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) =>
            prefix.StartsWith("TEST"u8) && encodedLength == length;

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            DecodeCalls++;
            return ValueTask.FromResult(RgbaFrame.CopyFrom([1, 2, 3, 255], 1, 1, VideoColorInfo.Srgb, allocator));
        }

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ImageInfo("length", 1, 1, ImageDimensionUnit.Pixels));
    }
}
