// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>Writes one complete image from borrowed pixels. Instances support concurrent independent calls.</summary>
    public interface IImageEncoder
    {
        string ImplementationId { get; }
        string FormatId { get; }
        ImageEncoderFeatures Features { get; }
        /// <summary>Retains input until completion, leaves the destination open and returns bytes written.
        /// Destinations need not support seeking. Failed output must be discarded.</summary>
        Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default);
    }
}
