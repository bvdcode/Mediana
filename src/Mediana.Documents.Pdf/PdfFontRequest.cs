// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>Identifies an unembedded PDF font requiring a caller-supplied OpenType program.</summary>
    /// <param name="PostScriptName">The BaseFont name declared by the PDF.</param>
    /// <param name="Composite">Whether the PDF font uses a composite character encoding.</param>
    public record PdfFontRequest(string PostScriptName, bool Composite);
}
