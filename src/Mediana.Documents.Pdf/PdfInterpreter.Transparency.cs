// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void PageGroup(PdfValue group)
        {
            if (group.Kind == PdfValueKind.Null) { return; }
            if (!TransparencyGroup(group)) { return; }
            PdfValue colorSpace = Get(group, "CS");
            if (colorSpace.Kind != PdfValueKind.Null && (colorSpace.Kind != PdfValueKind.Name || colorSpace.Text != "DeviceRGB"))
            {
                _unsupported.Add("Page group blending color space");
            }
            _ = GroupFlag(group, "I");
            if (GroupFlag(group, "K")) { _unsupported.Add("Knockout page group"); }
        }

        private void FormGroup(PdfValue group)
        {
            if (group.Kind == PdfValueKind.Null || !TransparencyGroup(group)) { return; }
            bool isolated = GroupFlag(group, "I"), knockout = GroupFlag(group, "K");
            if (isolated || knockout || _state.FillAlpha != 1 || _state.StrokeAlpha != 1
                || _state.Blend != Mediana.Drawing.ColorBlendMode.Normal)
            {
                _unsupported.Add("Form transparency group requiring separate composition");
            }
        }

        private bool TransparencyGroup(PdfValue group)
        {
            if (group.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF Group requires a dictionary."); }
            if (Get(group, "S").Name() == "Transparency") { return true; }
            _unsupported.Add("Unknown transparency group subtype");
            return false;
        }

        private bool GroupFlag(PdfValue group, string key)
        {
            PdfValue value = Get(group, key);
            if (value.Kind is not (PdfValueKind.Null or PdfValueKind.Boolean)) { throw new InvalidDataException("PDF group flags require booleans."); }
            return value.Number != 0;
        }
    }
}
