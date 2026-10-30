// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs
{
    /// <summary>Optional image-decoder capability for reading declarations without decoding pixels or rendering.
    /// Instances support concurrent calls. Encoded bytes are borrowed for the call; returned metadata owns no media buffers.
    /// Successful inspection does not guarantee that every encoded feature can be decoded.</summary>
    public interface IImageInfoReader
    {
        ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default);
    }
}
