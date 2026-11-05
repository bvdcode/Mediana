// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void Annotations(PdfValue[] references)
        {
            foreach (PdfValue reference in references)
            {
                token.ThrowIfCancellationRequested();
                PdfValue annotation = Resolve(reference);
                if (annotation.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF annotation requires a dictionary."); }
                PdfValue membership = Get(annotation, "OC");
                if (membership.Kind != PdfValueKind.Null && !optionalContent.IsVisible(membership, token, ref _operations)) { continue; }
                PdfValue flags = Get(annotation, "F");
                int value = flags.Kind == PdfValueKind.Null ? 0 : flags.Integer();
                if (value < 0) { throw new InvalidDataException("Invalid PDF annotation flags."); }
                if ((value & (2 | 32)) != 0) { continue; }
                double[] box = PdfDocument.Box(Get(annotation, "Rect"));
                if (box[0] == box[2] || box[1] == box[3]) { continue; }
                if (Get(annotation, "Subtype").Text == "Link" && Get(annotation, "AP").Kind == PdfValueKind.Null
                    && LinkBorderWidth(annotation) == 0)
                {
                    continue;
                }
                _unsupported.Add("Annotation appearance streams");
            }
        }

        private double LinkBorderWidth(PdfValue annotation)
        {
            PdfValue style = Get(annotation, "BS");
            if (style.Kind != PdfValueKind.Null)
            {
                if (style.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF border style requires a dictionary."); }
                PdfValue width = Get(style, "W");
                return width.Kind == PdfValueKind.Null ? 1 : width.Numeric();
            }
            PdfValue border = Get(annotation, "Border");
            if (border.Kind == PdfValueKind.Null) { return 1; }
            if (border.Kind != PdfValueKind.Array || border.Items.Length is not (3 or 4)) { throw new InvalidDataException("Invalid PDF annotation border."); }
            return Resolve(border.Items[2]).Numeric();
        }
    }
}
