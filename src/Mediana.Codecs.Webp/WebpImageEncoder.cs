// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    public class WebpImageEncoder(WebpEncodingOptions? options = null) : IImageEncoder
    {
        private readonly WebpEncodingOptions settings = options ?? new();
        public string ImplementationId => settings.Compression switch
        {
            WebpCompression.Lossless => "webp.managed.lossless.encoder",
            WebpCompression.Lossy => "webp.managed.lossy.encoder",
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
        public string FormatId => "webp";
        public ImageEncoderFeatures Features => ImageEncoderFeatures.None;
        public Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => WebpEncoder.EncodeAsync(input, destination, allocator, settings, cancellationToken);
    }
}
