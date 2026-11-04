// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void SelectColorSpace(string name, PdfValue resources, bool stroke)
        {
            if (_state.UncoloredGlyph) { return; }
            if (name is not ("DeviceGray" or "DeviceRGB" or "DeviceCMYK"))
            {
                PdfValue declaration = Get(Get(resources, "ColorSpace"), name);
                if (declaration.Kind != PdfValueKind.Name) { throw new NotSupportedException("Unsupported PDF color space: " + name); }
                name = declaration.Text;
            }
            PdfColorSpace space = name switch
            {
                "DeviceGray" => PdfColorSpace.DeviceGray,
                "DeviceRGB" => PdfColorSpace.DeviceRgb,
                "DeviceCMYK" => PdfColorSpace.DeviceCmyk,
                _ => throw new NotSupportedException("Unsupported PDF color space: " + name)
            };
            Color(new Rgba32(0, 0, 0), stroke, space);
        }

        private void SetColor(IReadOnlyList<PdfValue> operands, bool stroke)
        {
            if (_state.UncoloredGlyph) { return; }
            PdfColorSpace space = stroke ? _state.StrokeSpace : _state.FillSpace;
            int expected = space switch
            {
                PdfColorSpace.DeviceGray => 1,
                PdfColorSpace.DeviceRgb => 3,
                PdfColorSpace.DeviceCmyk => 4,
                _ => throw new InvalidDataException("Invalid PDF color space.")
            };
            if (operands.Count != expected) { throw new InvalidDataException("Invalid PDF color component count."); }
            double N(int i) => Resolve(operands[i]).Numeric();
            Rgba32 color = space switch
            {
                PdfColorSpace.DeviceGray => new(Channel(N(0)), Channel(N(0)), Channel(N(0))),
                PdfColorSpace.DeviceRgb => new(Channel(N(0)), Channel(N(1)), Channel(N(2))),
                PdfColorSpace.DeviceCmyk => new(Channel(1 - Math.Min(1, N(0) + N(3))), Channel(1 - Math.Min(1, N(1) + N(3))), Channel(1 - Math.Min(1, N(2) + N(3)))),
                _ => throw new InvalidDataException("Invalid PDF color space.")
            };
            Color(color, stroke);
        }
    }
}
