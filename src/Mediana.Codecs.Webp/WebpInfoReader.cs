// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs.Webp
{
    /// <summary>Inspects RIFF image/animation headers and ICC/Exif/XMP without decompressing image samples.</summary>
    public class WebpInfoReader : IImageInfoReader
    {
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WebpDocument(encoded, cancellationToken).Inspect(cancellationToken));
    }
}
