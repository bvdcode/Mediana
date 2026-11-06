// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
namespace Mediana.Documents.Presentation
{
    public record PresentationReaderOptions
    {
        public OpenXmlPackageOptions Package { get; init; } = new();
        public int MaximumSlides { get; init; } = 4096;
        public int MaximumShapes { get; init; } = 100_000;
        public int MaximumGlyphs { get; init; } = 1_000_000;
    }
}
