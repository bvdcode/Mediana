// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;

namespace Mediana.Documents.Word
{
    /// <summary>Package limits and explicit policies for incomplete document measurements.</summary>
    public record WordReaderOptions
    {
        public OpenXmlPackageOptions Package { get; init; } = new();
        public bool AllowFractionalMeasurements { get; init; }
        public WordPageSize? DefaultPageSize { get; init; }
        public int MaximumCharacters { get; init; } = 2_000_000;
        public int MaximumBlocks { get; init; } = 20_000;
    }
}
