// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

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
                if ((value & (8 | 16)) != 0)
                {
                    _unsupported.Add("Annotation fixed rotation or zoom");
                    continue;
                }
                PdfValue appearances = Get(annotation, "AP");
                if (appearances.Kind == PdfValueKind.Null)
                {
                    _unsupported.Add("Annotation without an appearance stream");
                    continue;
                }
                if (appearances.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("Invalid PDF annotation appearances."); }
                PdfValue normal = appearances.Get("N");
                PdfObject? appearance = normal.Kind == PdfValueKind.Reference ? storage.Object(normal, token) : null;
                if (appearance is null || !appearance.HasStream)
                {
                    PdfValue states = Resolve(normal);
                    PdfValue selected = Get(annotation, "AS");
                    if (states.Kind != PdfValueKind.Dictionary || selected.Kind != PdfValueKind.Name)
                    {
                        _unsupported.Add("Annotation without a selected normal appearance");
                        continue;
                    }
                    PdfValue state = states.Get(selected.Name());
                    if (state.Kind == PdfValueKind.Null) { throw new InvalidDataException("Missing selected PDF annotation appearance."); }
                    appearance = storage.Object(state, token);
                }
                if (!appearance.HasStream || Get(appearance.Value, "Subtype").Name() != "Form")
                { throw new InvalidDataException("PDF annotation appearance requires a Form stream."); }
                double[] bounds = PdfDocument.Box(Get(appearance.Value, "BBox"));
                Matrix3x2 matrix = appearance.Value.Get("Matrix").Kind == PdfValueKind.Null ? Matrix3x2.Identity : Matrix(appearance.Value.Get("Matrix"));
                Vector2 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
                foreach (Vector2 corner in new[] { new Vector2(Float(bounds[0]), Float(bounds[1])), new Vector2(Float(bounds[2]), Float(bounds[1])),
                    new Vector2(Float(bounds[2]), Float(bounds[3])), new Vector2(Float(bounds[0]), Float(bounds[3])) })
                {
                    Vector2 point = Vector2.Transform(corner, matrix);
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                }
                Vector2 size = maximum - minimum;
                if (size.X <= 0 || size.Y <= 0) { throw new InvalidDataException("Empty transformed PDF appearance bounds."); }
                Matrix3x2 placement = matrix * Matrix3x2.CreateTranslation(-minimum)
                    * Matrix3x2.CreateScale(Float(box[2] - box[0]) / size.X, Float(box[3] - box[1]) / size.Y)
                    * Matrix3x2.CreateTranslation(Float(box[0]), Float(box[1])) * _state.Transform;
                PaintForm(appearance, _pageResources, placement, 0);
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
