// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>Separate limits for encoded data, decoded streams and structural complexity.</summary>
    public record PdfReaderOptions
    {
        public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumDecodedBytes { get; init; } = 128 * 1024 * 1024;
        public int MaximumObjects { get; init; } = 100_000;
        public int MaximumValues { get; init; } = 2_000_000;
        public int MaximumDepth { get; init; } = 64;
        public int MaximumPages { get; init; } = 10_000;
        public int MaximumOperations { get; init; } = 1_000_000;
        public int MaximumImagePixels { get; init; } = 16_777_216;
        public int MaximumImageBytes { get; init; } = 128 * 1024 * 1024;
    }
}
