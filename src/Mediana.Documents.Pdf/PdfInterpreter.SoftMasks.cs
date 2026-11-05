// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private PdfSoftMask? ReadSoftMask(PdfValue dictionary, PdfValue resources, int depth)
        {
            if (dictionary.Kind == PdfValueKind.Name && dictionary.Text == "None") { return null; }
            if (dictionary.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF soft mask requires a dictionary."); }
            bool luminosity = Get(dictionary, "S").Name() switch
            {
                "Alpha" => false,
                "Luminosity" => true,
                _ => throw new NotSupportedException("Unknown PDF soft mask subtype.")
            };
            PdfObject form = storage.Object(dictionary.Get("G"), token);
            if (!form.HasStream || Get(form.Value, "Subtype").Name() != "Form")
            { throw new InvalidDataException("PDF soft mask requires a Form stream."); }
            PdfValue group = Get(form.Value, "Group");
            bool gray = GroupUsesGray(group);
            Rgba32 background = new(0, 0, 0);
            PdfValue declared = Get(dictionary, "BC");
            if (declared.Kind != PdfValueKind.Null && luminosity)
            {
                double[] colors = Numbers(declared, gray ? 1 : 3);
                if (gray) { background = new(Channel(colors[0]), Channel(colors[0]), Channel(colors[0])); }
                else { background = new(Channel(colors[0]), Channel(colors[1]), Channel(colors[2])); }
            }
            byte[] transfer = new byte[256];
            PdfValue mapping = Get(dictionary, "TR");
            PdfFunction? function = mapping.Kind == PdfValueKind.Null || mapping.Kind == PdfValueKind.Name && mapping.Text == "Identity"
                ? null : ReadFunction(mapping);
            if (function is not null && function.OutputCount != 1) { throw new InvalidDataException("PDF mask transfer requires one output."); }
            if (function is not null) { ChargeOperations(checked(transfer.Length * function.EvaluationCost)); }
            Span<double> output = stackalloc double[1];
            for (int i = 0; i < transfer.Length; i++)
            {
                if (function is null) { transfer[i] = (byte)i; }
                else { function.Evaluate(i / 255d, output); transfer[i] = Channel(output[0]); }
            }
            if (!_forms.Add(form)) { throw new InvalidDataException("Recursive PDF soft mask."); }
            try
            {
                Matrix3x2 transform = _state.Transform;
                if (form.Value.Get("Matrix").Kind != PdfValueKind.Null) { transform = Matrix(form.Value.Get("Matrix")) * transform; }
                PdfValue local = form.Value.Get("Resources").Kind == PdfValueKind.Null ? resources : Get(form.Value, "Resources");
                return new PdfSoftMask(CaptureGroup(form, local, transform, depth, group, true), luminosity, background, transfer);
            }
            finally { _forms.Remove(form); }
        }
    }
}
