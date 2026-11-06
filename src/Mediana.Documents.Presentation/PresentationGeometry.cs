// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal static class PresentationGeometry
    {
        internal static (double Width, double Height, Matrix3x2 Transform) Placement(XElement xfrm)
        {
            XNamespace a = xfrm.Elements().FirstOrDefault()?.Name.Namespace ?? xfrm.Name.Namespace;
            XElement off = xfrm.Element(a + "off") ?? throw new InvalidDataException("Missing presentation shape offset.");
            XElement ext = xfrm.Element(a + "ext") ?? throw new InvalidDataException("Missing presentation shape extent.");
            double x = PresentationXml.Points(off, "x"), y = PresentationXml.Points(off, "y");
            double w = PresentationXml.Points(ext, "cx"), h = PresentationXml.Points(ext, "cy");
            if (w < 0 || h < 0) { throw new InvalidDataException("Negative presentation shape extent."); }
            Matrix3x2 matrix = Matrix3x2.CreateTranslation((float)-w / 2, (float)-h / 2)
                * Matrix3x2.CreateScale(PresentationXml.Boolean(xfrm, "flipH") ? -1 : 1, PresentationXml.Boolean(xfrm, "flipV") ? -1 : 1)
                * Matrix3x2.CreateRotation((float)(PresentationXml.Number(xfrm, "rot") * Math.PI / (60000 * 180)))
                * Matrix3x2.CreateTranslation((float)(x + w / 2), (float)(y + h / 2));
            return (w, h, matrix);
        }
        internal static Matrix3x2 GroupTransform(XElement xfrm)
        {
            (double w, double h, Matrix3x2 placement) = Placement(xfrm);
            XNamespace a = xfrm.Elements().FirstOrDefault()?.Name.Namespace ?? xfrm.Name.Namespace;
            XElement off = xfrm.Element(a + "chOff") ?? throw new InvalidDataException("Missing group child offset.");
            XElement ext = xfrm.Element(a + "chExt") ?? throw new InvalidDataException("Missing group child extent.");
            double cw = PresentationXml.Points(ext, "cx"), ch = PresentationXml.Points(ext, "cy");
            if (cw <= 0 || ch <= 0) { throw new InvalidDataException("Invalid group child extent."); }
            return Matrix3x2.CreateTranslation((float)-PresentationXml.Points(off, "x"), (float)-PresentationXml.Points(off, "y"))
                * Matrix3x2.CreateScale((float)(w / cw), (float)(h / ch)) * placement;
        }
        internal static VectorPath Path(XElement? geometry, double width, double height, bool connector)
        {
            float w = (float)width, h = (float)height;
            if (geometry?.Name.LocalName == "custGeom") { return PresentationCustomGeometry.Read(geometry, w, h); }
            string preset = (string?)geometry?.Attribute("prst") ?? (connector ? "line" : "rect");
            VectorPath path = new();
            switch (preset)
            {
                case "rect": return path.Rectangle(0, 0, w, h);
                case "line": case "straightConnector1": return path.MoveTo(0, 0).LineTo(w, h);
                case "ellipse":
                    const float k = .55228475f;
                    return path.MoveTo(w, h / 2).CubicTo(w, h / 2 + k * h / 2, w / 2 + k * w / 2, h, w / 2, h)
                        .CubicTo(w / 2 - k * w / 2, h, 0, h / 2 + k * h / 2, 0, h / 2).CubicTo(0, h / 2 - k * h / 2, w / 2 - k * w / 2, 0, w / 2, 0)
                        .CubicTo(w / 2 + k * w / 2, 0, w, h / 2 - k * h / 2, w, h / 2).Close();
                case "triangle": return path.MoveTo(w / 2, 0).LineTo(w, h).LineTo(0, h).Close();
                case "rtTriangle": return path.MoveTo(0, 0).LineTo(w, h).LineTo(0, h).Close();
                case "diamond": return path.MoveTo(w / 2, 0).LineTo(w, h / 2).LineTo(w / 2, h).LineTo(0, h / 2).Close();
                case "roundRect":
                    double adj = Adjustment(geometry, "adj", 16667) / 100000;
                    float r = Math.Min(w, h) * (float)Math.Clamp(adj, 0, .5);
                    const float c = .55228475f;
                    return path.MoveTo(r, 0).LineTo(w - r, 0).CubicTo(w - r + c * r, 0, w, r - c * r, w, r).LineTo(w, h - r)
                        .CubicTo(w, h - r + c * r, w - r + c * r, h, w - r, h).LineTo(r, h).CubicTo(r - c * r, h, 0, h - r + c * r, 0, h - r)
                        .LineTo(0, r).CubicTo(0, r - c * r, r - c * r, 0, r, 0).Close();
                case "rightArrow":
                case "leftArrow":
                case "upArrow":
                case "downArrow":
                    bool vertical = preset is "upArrow" or "downArrow";
                    float aw = vertical ? h : w, ah = vertical ? w : h;
                    float shaft = ah * (float)Math.Clamp(Adjustment(geometry, "adj1", 50000) / 100000, 0, 1);
                    float head = Math.Min(aw, Math.Min(aw, ah) * (float)Math.Max(0, Adjustment(geometry, "adj2", 50000) / 100000));
                    path.MoveTo(0, (ah - shaft) / 2).LineTo(aw - head, (ah - shaft) / 2).LineTo(aw - head, 0).LineTo(aw, ah / 2)
                        .LineTo(aw - head, ah).LineTo(aw - head, (ah + shaft) / 2).LineTo(0, (ah + shaft) / 2).Close();
                    return preset switch
                    {
                        "rightArrow" => path,
                        "leftArrow" => path.Transform(new(-1, 0, 0, 1, w, 0)),
                        "downArrow" => path.Transform(new(0, 1, 1, 0, 0, 0)),
                        "upArrow" => path.Transform(new(0, -1, 1, 0, 0, h)),
                        _ => throw new InvalidOperationException()
                    };
                default: throw new NotSupportedException("Unsupported DrawingML preset geometry: " + preset);
            }
        }
        private static double Adjustment(XElement? geometry, string name, double defaultValue)
        {
            string? formula = (string?)geometry?.Elements().FirstOrDefault(item => item.Name.LocalName == "avLst")?
                .Elements().FirstOrDefault(item => (string?)item.Attribute("name") == name)?.Attribute("fmla");
            if (formula is null) { return defaultValue; }
            if (!formula.StartsWith("val ", StringComparison.Ordinal)) { throw new NotSupportedException("Unsupported preset adjustment formula."); }
            return PresentationXml.Number(new XElement("v", new XAttribute("n", formula[4..])), "n");
        }
    }
}
