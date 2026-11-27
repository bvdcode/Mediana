// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Images.Metadata
{
    internal interface IIndexedRawPreviewReader
    {
        ValueTask<RawImagePreview?> ExtractAsync(IRandomAccessByteSource source, int length, int requestedSize, CancellationToken token);
    }
}
