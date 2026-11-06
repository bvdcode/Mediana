// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Documents.Presentation
{
    internal record PresentationTextParagraph(IReadOnlyList<PresentationTextAtom> Atoms, IReadOnlyList<XElement> Styles,
        double MarkSize, double Indent, double MarginLeft, double MarginRight, string Alignment);
}
