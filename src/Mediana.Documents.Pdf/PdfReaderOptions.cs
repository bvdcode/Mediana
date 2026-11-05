// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>Separate limits for encoded data, decoded streams and structural complexity.</summary>
    public record PdfReaderOptions
    {
        /// <summary>Supplies an OpenType font program for an unembedded PDF font. Empty memory declines the request.
        /// Character mappings and declared advances remain controlled by the PDF. No fonts are selected automatically.</summary>
        public Func<PdfFontRequest, ReadOnlyMemory<byte>>? FontResolver { get; init; }

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
    }
}
