// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Memory;

namespace Mediana.Documents.Pdf
{
    /// <summary>Separate limits for encoded data, decoded streams and structural complexity.</summary>
    public record PdfReaderOptions
    {
        /// <summary>Shared external font resources. Embedded PDF fonts, encodings and declared advances remain authoritative.
        /// Inline artwork does not replace encoded PDF glyphs.</summary>
        public TextOptions Text { get; init; } = new();

        public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumDecodedBytes { get; init; } = 128 * 1024 * 1024;
        public int MaximumObjects { get; init; } = 100_000;
        public int MaximumValues { get; init; } = 2_000_000;
        public int MaximumDepth { get; init; } = 64;
        public int MaximumPages { get; init; } = 10_000;
        public int MaximumOperations { get; init; } = 1_000_000;
        /// <summary>Maximum encoded pixel count per source image. Rendering may reduce JPEG samples before allocation.</summary>
        public int MaximumImagePixels { get; init; } = 64 * 1024 * 1024;
        public int MaximumImageBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Optional shared budget for retained image pixels and JPEG workspace.</summary>
        public MediaBufferAllocator? ImageAllocator { get; init; }
    }
}
