// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal class FixtureImageDecoder(string id = "test.image",
        Func<ReadOnlyMemory<byte>, ImageDecodeRequest, MediaBufferAllocator, CancellationToken, ValueTask<VideoFrame>>? decode = null) : IImageDecoder
    {
        public string ImplementationId => id;
        public int ProbeLength { get; init; } = 4;
        public ImageDecoderFeatures Features { get; init; }
        public int DecodeCalls { get; private set; }
        public byte[]? Received { get; private set; }
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith("TEST"u8);
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            DecodeCalls++;
            Received = encoded.ToArray();
            return decode is null
                ? ValueTask.FromResult(RgbaFrame.CopyFrom([1, 2, 3, 4], 1, 1, VideoColorInfo.Srgb, allocator))
                : decode(encoded, request, allocator, cancellationToken);
        }
    }
}
