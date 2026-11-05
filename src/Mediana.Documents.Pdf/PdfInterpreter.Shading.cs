// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void Shading(string name, PdfValue resources)
        {
            if (!_contentVisible) { return; }
            if (_state.UncoloredGlyph) { throw new InvalidDataException("An uncolored Type 3 glyph cannot paint shading."); }
            PdfValue shading = Get(Get(resources, "Shading"), name);
            int type = Get(shading, "ShadingType").Integer();
            if (type is not (2 or 3)) { throw new NotSupportedException("Unsupported PDF shading type: " + type); }
            string space = Get(shading, "ColorSpace").Name();
            int components = space switch
            {
                "DeviceGray" => 1,
                "DeviceRGB" => 3,
                "DeviceCMYK" => 4,
                _ => throw new NotSupportedException("Unsupported PDF shading color space: " + space)
            };
            double[] coordinates = Numbers(Get(shading, "Coords"), type == 2 ? 4 : 6);
            foreach (double coordinate in coordinates) { _ = Float(coordinate); }
            if (type == 3 && (coordinates[2] < 0 || coordinates[5] < 0)) { throw new InvalidDataException("Negative PDF shading radius."); }
            double[] domain = shading.Get("Domain").Kind == PdfValueKind.Null ? [0, 1] : Numbers(Get(shading, "Domain"), 2);
            if (domain[0] >= domain[1]) { throw new InvalidDataException("Invalid PDF shading domain."); }
            bool start = false, end = false;
            PdfValue extend = Get(shading, "Extend");
            if (extend.Kind != PdfValueKind.Null)
            {
                if (extend.Kind != PdfValueKind.Array || extend.Items.Length != 2 || extend.Items.Any(item => item.Kind != PdfValueKind.Boolean))
                { throw new InvalidDataException("Invalid PDF shading extension flags."); }
                start = extend.Items[0].Number != 0;
                end = extend.Items[1].Number != 0;
            }
            PdfFunction function = ReadFunction(shading.Get("Function"));
            if (function.OutputCount != components) { throw new InvalidDataException("PDF shading function does not match its color space."); }
            const int colorSamples = 2049;
            ChargeOperations(checked(colorSamples * function.EvaluationCost));
            Rgba32[] colors = new Rgba32[colorSamples];
            Span<double> values = stackalloc double[4];
            for (int i = 0; i < colors.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                function.Evaluate(domain[0] + ((domain[1] - domain[0]) * i / (colors.Length - 1)), values);
                byte alpha = Channel(_state.FillAlpha);
                colors[i] = space switch
                {
                    "DeviceGray" => new(Channel(values[0]), Channel(values[0]), Channel(values[0]), alpha),
                    "DeviceRGB" => new(Channel(values[0]), Channel(values[1]), Channel(values[2]), alpha),
                    "DeviceCMYK" => new(Channel(1 - Math.Min(1, values[0] + values[3])), Channel(1 - Math.Min(1, values[1] + values[3])), Channel(1 - Math.Min(1, values[2] + values[3])), alpha),
                    _ => throw new InvalidDataException("Invalid PDF shading color space.")
                };
            }
            VectorPath? bounds = null;
            if (Get(shading, "BBox").Kind != PdfValueKind.Null)
            {
                double[] box = PdfDocument.Box(Get(shading, "BBox"));
                bounds = new VectorPath().Rectangle(Float(box[0]), Float(box[1]), Float(box[2] - box[0]), Float(box[3] - box[1]));
            }
            AddPaint(new ShadingCommand(type == 3, coordinates, start, end, colors, _state.Transform, bounds, _state.Blend));
        }
    }
}
