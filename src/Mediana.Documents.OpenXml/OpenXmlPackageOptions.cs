// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.OpenXml
{
    /// <summary>Bounds for read-only OPC packages. No package parts are extracted to disk.</summary>
    public record OpenXmlPackageOptions
    {
        public int MaximumInputBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumParts { get; init; } = 2048;
        public int MaximumPartBytes { get; init; } = 32 * 1024 * 1024;
        public long MaximumUncompressedBytes { get; init; } = 128L * 1024 * 1024;
        public int MaximumXmlDepth { get; init; } = 64;
        public int MaximumXmlElements { get; init; } = 500_000;
    }
}
