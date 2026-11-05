// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    public class WebpImageEncoder : IImageEncoder
    {
        public string ImplementationId => "webp.managed.lossless.encoder";
        public string FormatId => "webp";
        public ImageEncoderFeatures Features => ImageEncoderFeatures.None;
        public Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => WebpEncoder.EncodeAsync(input, destination, allocator, cancellationToken);
    }
}
