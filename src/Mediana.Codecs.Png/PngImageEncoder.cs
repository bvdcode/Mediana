// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    public class PngImageEncoder : IImageEncoder
    {
        public string ImplementationId => "png.managed.encoder";
        public string FormatId => "png";
        public Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => PngEncoder.EncodeAsync(input, destination, allocator, cancellationToken);
    }
}
