// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    public interface IImagePreviewReader
    {
        /// <summary>Returns original encoded previews in storage order without decoding or re-encoding.
        /// Returned slices borrow input memory; its owner must keep that memory valid and unchanged while any preview is used.</summary>
        ValueTask<IReadOnlyList<EncodedImagePreview>> ReadPreviewsAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default);
    }
}
