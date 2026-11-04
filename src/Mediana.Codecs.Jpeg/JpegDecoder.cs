// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Jpeg
{
    public static class JpegDecoder
    {
        /// <summary>Decodes native 8-bit components for embedding formats that define their own presentation.
        /// Borrows encoded input only until completion. Ignores application metadata except the Adobe APP14
        /// transform declaration. Does not infer color, apply orientation or allocate an RGBA output.
        /// The caller disposes returned component storage; all samples/coefficients share the allocator.</summary>
        public static JpegImage DecodeComponents(ReadOnlyMemory<byte> encoded, MediaBufferAllocator allocator,
            JpegDecodeOptions? options = null, CancellationToken cancellationToken = default) =>
            DecodeReducedComponents(encoded, allocator, 1, options, cancellationToken);

        /// <summary>Decodes native components at 1/1, 1/2, 1/4 or 1/8 resolution by averaging within each DCT block.
        /// Output dimensions round up. MaximumPixels still limits the encoded dimensions. Sample storage scales
        /// with output resolution; progressive coefficient storage remains full size and charged to the allocator.</summary>
        public static JpegImage DecodeReducedComponents(ReadOnlyMemory<byte> encoded, MediaBufferAllocator allocator,
            int reduction, JpegDecodeOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            if (reduction is not (1 or 2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(reduction)); }
            options ??= new();
            options.Validate();
            using JpegDocument image = new(encoded, options, null, null, cancellationToken, reduction);
            image.Read(allocator, componentSamples: true);
            cancellationToken.ThrowIfCancellationRequested();
            return new(image);
        }
    }
}
