// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>A requested Word font face. The resolver returns an OpenType or TrueType font program.</summary>
    public record WordFontRequest(string Family, bool Bold, bool Italic);
}
