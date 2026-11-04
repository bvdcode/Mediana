// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void PageGroup(PdfValue group)
        {
            if (group.Kind == PdfValueKind.Null) { return; }
            if (group.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF page Group requires a dictionary."); }
            if (Get(group, "S").Text != "Transparency") { _unsupported.Add("Unknown page group subtype"); return; }
            PdfValue colorSpace = Get(group, "CS");
            if (colorSpace.Kind != PdfValueKind.Null && (colorSpace.Kind != PdfValueKind.Name || colorSpace.Text != "DeviceRGB"))
            {
                _unsupported.Add("Page group blending color space");
            }
            PdfValue isolated = Get(group, "I"), knockout = Get(group, "K");
            if (isolated.Kind is not (PdfValueKind.Null or PdfValueKind.Boolean) || knockout.Kind is not (PdfValueKind.Null or PdfValueKind.Boolean))
            {
                throw new InvalidDataException("PDF page group flags require booleans.");
            }
            if (knockout.Number != 0) { _unsupported.Add("Knockout page group"); }
        }
    }
}
