// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;

namespace Mediana.Images.Metadata
{
    /// <summary>Metadata and encoded preview inspection without raster decoding. Returned previews borrow the supplied bytes.</summary>
    public abstract class ImageMetadataReader : IImageInspector, IImagePreviewReader
    {
        public abstract string ImplementationId { get; }
        public abstract int ProbeLength { get; }
        public abstract bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null);
        public abstract ImageInspection Inspect(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Inspect(encoded, cancellationToken).Info);
        public ValueTask<IReadOnlyList<EncodedImagePreview>> ReadPreviewsAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Inspect(encoded, cancellationToken).Previews);
    }
}
