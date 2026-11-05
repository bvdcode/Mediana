// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    /// <summary>Font selection and bounds for device-independent document pagination.</summary>
    public record WordLayoutOptions
    {
        /// <summary>Shared font, glyph and inline artwork resources for this layout.</summary>
        public TextOptions Text { get; init; } = new();
        public int MaximumPages { get; init; } = 2048;
        public int MaximumGlyphs { get; init; } = 500_000;
        public int MaximumPathCommands { get; init; } = 2_000_000;
    }
}
