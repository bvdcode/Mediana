// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    public class JpegImageEncoder(JpegEncodingOptions? options = null) : IImageEncoder
    {
        private readonly JpegEncodingOptions settings = options ?? new();
        public string ImplementationId => "jpeg.managed.baseline.encoder";
        public string FormatId => "jpeg";
        public ImageEncoderFeatures Features => ImageEncoderFeatures.SampleAspectRatio;
        public Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => JpegEncoder.EncodeAsync(input, destination, allocator, settings, cancellationToken);
    }
}
