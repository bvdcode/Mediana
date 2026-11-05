// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class EmbeddedTestImageDecoder(bool recursive = false, Action? decoded = null) : IImageDecoder
    {
        public string ImplementationId => "embedded.reference.image";
        public int ProbeLength => 3;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("PIX"u8);

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (recursive) { return await request.Images!.DecodeAsync(encoded, request, allocator, cancellationToken); }
            VideoFrame frame = RgbaFrame.CopyFrom([21, 43, 65, 255], 1, 1, VideoColorInfo.Srgb, allocator, SampleAspectRatio.Square);
            decoded?.Invoke();
            return frame;
        }
    }
}
