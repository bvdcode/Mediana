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
            JpegDecodeOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            using JpegDocument image = new(encoded, options, null, null, cancellationToken);
            image.Read(allocator, componentSamples: true);
            cancellationToken.ThrowIfCancellationRequested();
            return new(image);
        }
    }
}
