// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    /// <summary>A complete-file image, page or animation decoder. Instances must support concurrent independent calls.</summary>
    public interface IImageDecoder
    {
        string ImplementationId { get; }
        int ProbeLength { get; }
        ImageDecoderFeatures Features { get; }
        /// <summary>Recognizes an input prefix; recognition is not a promise of complete-file support.</summary>
        bool CanDecode(ReadOnlySpan<byte> prefix);
        /// <summary>Borrows encoded bytes only during the call. Returns an independently owned frame.
        /// The allocator accounts for frame leases; codec workspace has module-specific limits.</summary>
        ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default);
    }
}
