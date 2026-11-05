// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>Font selection and bounds for device-independent document pagination.</summary>
    public record WordLayoutOptions
    {
        /// <summary>Returns the requested font program, or empty if unavailable. No files or network resources are opened implicitly.</summary>
        public Func<WordFontRequest, ReadOnlyMemory<byte>>? FontResolver { get; init; }
        /// <summary>Returns a font containing a missing Unicode character, or empty. Used only when substitution is enabled.</summary>
        public Func<WordFontRequest, int, ReadOnlyMemory<byte>>? FallbackFontResolver { get; init; }
        /// <summary>Permits replacing unavailable faces with the bundled CC0 Canada1500 family. Substitutions are reported in layout warnings.</summary>
        public bool SubstituteMissingFonts { get; init; }
        public int MaximumPages { get; init; } = 2048;
        public int MaximumGlyphs { get; init; } = 500_000;
        public int MaximumFontFaces { get; init; } = 64;
        public int MaximumPathCommands { get; init; } = 2_000_000;
    }
}
