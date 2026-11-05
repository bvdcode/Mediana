// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    public class WebpImageSequenceEncoder(WebpEncodingOptions? options = null, ushort loopCount = 0) : IImageSequenceEncoder
    {
        private readonly WebpEncodingOptions encoding = options ?? new();
        public string ImplementationId => "webp.managed.animation";
        public string FormatId => "webp";
        public TimeBase DurationTimeBase => new(1, 1000);

        public Task<long> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            WebpAnimationEncoder.EncodeAsync(frames, destination, allocator, encoding, loopCount, cancellationToken);
    }
}
