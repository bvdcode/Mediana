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

        private bool _grayGroup;

        private GroupCommand CaptureGroup(PdfObject obj, PdfValue resources, System.Numerics.Matrix3x2 transform,
            int depth, PdfValue group, bool mask = false)
        {
            if (!TransparencyGroup(group)) { throw new NotSupportedException("Unknown PDF group subtype."); }
            bool isolated = GroupFlag(group, "I"), knockout = GroupFlag(group, "K");
            bool gray = GroupUsesGray(group);
            PdfGraphicsState saved = _state;
            bool savedGray = _grayGroup;
            int start = _commands.Count;
            try
            {
                _grayGroup = gray;
                _state = _state with { FillAlpha = 1, StrokeAlpha = 1, Blend = Mediana.Drawing.ColorBlendMode.Normal, SoftMask = null };
                ExecuteNested(obj, resources, transform, depth + 1, boundingBox: Get(obj.Value, "BBox"));
                return new GroupCommand(_commands.Skip(start).ToArray(), mask ? 1 : saved.FillAlpha,
                    mask ? Mediana.Drawing.ColorBlendMode.Normal : saved.Blend, isolated, knockout, gray);
            }
            finally
            {
                _commands.RemoveRange(start, _commands.Count - start);
                _state = saved;
                _grayGroup = savedGray;
            }
        }

        private bool GroupUsesGray(PdfValue group)
        {
            PdfValue space = Get(group, "CS");
            return space.Kind switch
            {
                PdfValueKind.Null => _grayGroup,
                PdfValueKind.Name => space.Name() switch
                {
                    "DeviceRGB" => false,
                    "DeviceGray" => true,
                    _ => throw new NotSupportedException("Unsupported PDF group blending color space: " + space.Text)
                },
                _ => throw new NotSupportedException("Unsupported PDF group blending color space.")
            };
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
