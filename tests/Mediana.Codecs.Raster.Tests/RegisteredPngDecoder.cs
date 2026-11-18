// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    internal class RegisteredPngDecoder : IImageDecoder
    {
        private readonly PngImageDecoder decoder = new();
        public int Calls { get; private set; }
        public string ImplementationId => "registered.png";
        public int ProbeLength => decoder.ProbeLength;
        public ImageDecoderFeatures Features => decoder.Features;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => decoder.CanDecode(prefix, encodedLength);
        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            return decoder.Decode(encoded, request, allocator, cancellationToken);
        }
    }
}
