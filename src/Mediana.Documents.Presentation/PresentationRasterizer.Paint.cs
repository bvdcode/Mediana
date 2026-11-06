// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private static XElement? Fill(XElement? properties) => properties?.Elements().FirstOrDefault(item => item.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill");
        private async ValueTask PaintAsync(VectorPath path, XElement? fill, Matrix3x2 transform, XElement? reference = null)
        {
            Rgba32? placeholder = null;
            if (fill?.Name.LocalName == "bgRef") { reference = fill; fill = null; }
            if (reference is not null)
            {
                XElement? color = reference.Elements().FirstOrDefault();
                if (color is not null) { placeholder = scene.Theme.Color(color); }
                if (fill is null)
                {
                    int index = checked((int)PresentationXml.Number(reference, "idx"));
                    fill = scene.Theme.Style("fillRef", index);
                }
            }
            if (fill is null || fill.Name.LocalName == "noFill") { return; }
            switch (fill.Name.LocalName)
            {
                case "solidFill": canvas.FillPath(path, scene.Theme.Color(fill.Elements().Single(), placeholder), transform, cancellationToken: token); break;
                case "gradFill": Gradient(path, fill, placeholder, transform); break;
                case "blipFill": await ImageFillAsync(path, fill, transform).ConfigureAwait(false); break;
                default: throw new NotSupportedException("Unsupported presentation fill: " + fill.Name.LocalName);
            }
        }
        private void Gradient(VectorPath path, XElement gradient, Rgba32? placeholder, Matrix3x2 transform)
        {
            XNamespace a = gradient.Name.Namespace;
            (float Position, Rgba32 Color)[] stops = (gradient.Element(a + "gsLst")?.Elements(a + "gs") ?? [])
                .Select(item => ((float)(PresentationXml.Number(item, "pos") / 100000), scene.Theme.Color(item.Elements().Single(), placeholder)))
                .OrderBy(item => item.Item1).ToArray();
            if (stops.Length == 0) { throw new InvalidDataException("Empty presentation gradient."); }
            XElement linear = gradient.Element(a + "lin") ?? throw new NotSupportedException("Only linear presentation gradients are supported.");
            double angle = PresentationXml.Number(linear, "ang") * Math.PI / (60000 * 180);
            (float x, float y, float w, float h) = path.GetBounds(token);
            if (w == 0 || h == 0) { return; }
            float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
            if (PresentationXml.Boolean(linear, "scaled", true)) { dx *= w; dy *= h; }
            Vector2 axis = Vector2.Normalize(new(dx, dy));
            float span = Math.Abs(axis.X * w) + Math.Abs(axis.Y * h);
            canvas.Save();
            try
            {
                canvas.ClipPath(path, transform, cancellationToken: token);
                canvas.DrawShader(point => Sample((Vector2.Dot(point - new Vector2(x + w / 2, y + h / 2), axis) / span) + .5f), transform, cancellationToken: token);
            }
            finally { canvas.Restore(); }
            Rgba32 Sample(float fraction)
            {
                int i = 0;
                while (i < stops.Length && stops[i].Position <= fraction) { i++; }
                if (i == 0) { return stops[0].Color; }
                if (i == stops.Length) { return stops[^1].Color; }
                (float p, Rgba32 first) = stops[i - 1]; (float q, Rgba32 second) = stops[i];
                float t = q == p ? 1 : (fraction - p) / (q - p);
                byte Mix(byte c, byte d) => (byte)Math.Clamp(MathF.Round(c + (d - c) * t), 0, 255);
                return new(Mix(first.R, second.R), Mix(first.G, second.G), Mix(first.B, second.B), Mix(first.A, second.A));
            }
        }
        private void Stroke(VectorPath path, XElement? properties, XElement? style, Matrix3x2 transform)
        {
            XElement? line = properties?.Elements().FirstOrDefault(item => item.Name.LocalName == "ln");
            Rgba32? placeholder = null;
            XElement? reference = style?.Elements().FirstOrDefault(item => item.Name.LocalName == "lnRef");
            if (line is null && reference is not null)
            {
                line = scene.Theme.Style("lnRef", checked((int)PresentationXml.Number(reference, "idx")));
                if (reference.Elements().FirstOrDefault() is XElement color) { placeholder = scene.Theme.Color(color); }
            }
            XElement? fill = Fill(line);
            if (line is null || fill is null || fill.Name.LocalName == "noFill") { return; }
            if (fill.Name.LocalName != "solidFill") { throw new NotSupportedException("Only solid presentation outlines are supported."); }
            XNamespace a = line.Name.Namespace;
            float width = (float)PresentationXml.Points(line, "w", 1);
            if (width < 0) { throw new InvalidDataException("Negative presentation outline width."); }
            string dash = (string?)line.Element(a + "prstDash")?.Attribute("val") ?? "solid";
            IReadOnlyList<float> dashes = dash switch
            {
                "solid" => [],
                "dot" or "sysDot" => new[] { width, width },
                "dash" or "sysDash" => new[] { 3 * width, 3 * width },
                "lgDash" => new[] { 8 * width, 3 * width },
                "dashDot" or "sysDashDot" => new[] { 3 * width, 3 * width, width, 3 * width },
                _ => throw new NotSupportedException("Unsupported presentation outline dash: " + dash)
            };
            StrokeCap cap = (string?)line.Attribute("cap") switch { null or "flat" => StrokeCap.Butt, "rnd" => StrokeCap.Round, "sq" => StrokeCap.Square, _ => throw new InvalidDataException("Invalid presentation line cap.") };
            StrokeJoin join = line.Element(a + "round") is not null ? StrokeJoin.Round : line.Element(a + "bevel") is not null ? StrokeJoin.Bevel : StrokeJoin.Miter;
            canvas.StrokePath(path, scene.Theme.Color(fill.Elements().Single(), placeholder), new() { Width = width, Cap = cap, Join = join, Dashes = dashes }, transform, cancellationToken: token);
        }
    }
}
