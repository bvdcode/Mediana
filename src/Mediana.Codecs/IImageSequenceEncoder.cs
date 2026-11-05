// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>Encodes borrowed full-canvas frames in display order. Instances support concurrent independent calls.
    /// Frame durations must be exact multiples of DurationTimeBase. Leaves the destination open; failed output must be discarded.</summary>
    public interface IImageSequenceEncoder
    {
        string ImplementationId { get; }
        string FormatId { get; }
        TimeBase DurationTimeBase { get; }
        Task<long> EncodeAsync(IAsyncEnumerable<VideoFrame> frames, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default);
    }
}
