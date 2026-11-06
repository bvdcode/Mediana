// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal static class PresentationCustomGeometry
    {
        internal static VectorPath Read(XElement geometry, float width, float height)
        {
            XNamespace a = geometry.Name.Namespace;
            Dictionary<string, double> values = new(StringComparer.Ordinal) { ["w"] = width * PresentationXml.EmuPerPoint, ["h"] = height * PresentationXml.EmuPerPoint };
            foreach (XElement guide in (geometry.Element(a + "avLst")?.Elements() ?? []).Concat(geometry.Element(a + "gdLst")?.Elements() ?? []))
            {
                string name = (string?)guide.Attribute("name") ?? throw new InvalidDataException("Missing DrawingML guide name.");
                string[] f = ((string?)guide.Attribute("fmla") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double value = f.Length switch
                {
                    2 when f[0] == "val" => Value(f[1]),
                    4 when f[0] == "*/" => Value(f[1]) * Value(f[2]) / Value(f[3]),
                    4 when f[0] == "+-" => Value(f[1]) + Value(f[2]) - Value(f[3]),
                    4 when f[0] == "+/" => (Value(f[1]) + Value(f[2])) / Value(f[3]),
                    3 when f[0] == "min" => Math.Min(Value(f[1]), Value(f[2])),
                    3 when f[0] == "max" => Math.Max(Value(f[1]), Value(f[2])),
                    _ => throw new NotSupportedException("Unsupported DrawingML guide formula.")
                };
                if (!double.IsFinite(value)) { throw new InvalidDataException("Nonfinite DrawingML guide."); }
                values[name] = value;
            }
            VectorPath result = new();
            foreach (XElement source in geometry.Element(a + "pathLst")?.Elements(a + "path") ?? [])
            {
                double w = PresentationXml.Number(source, "w", values["w"]), h = PresentationXml.Number(source, "h", values["h"]);
                if (w <= 0 || h <= 0) { throw new InvalidDataException("Invalid DrawingML path coordinate space."); }
                VectorPath path = new();
                foreach (XElement command in source.Elements())
                {
                    Vector2[] points = command.Elements(a + "pt").Select(item => new Vector2((float)Value((string?)item.Attribute("x") ?? ""), (float)Value((string?)item.Attribute("y") ?? ""))).ToArray();
                    switch (command.Name.LocalName)
                    {
                        case "moveTo" when points.Length == 1: path.MoveTo(points[0].X, points[0].Y); break;
                        case "lnTo" when points.Length == 1: path.LineTo(points[0].X, points[0].Y); break;
                        case "quadBezTo" when points.Length == 2: path.QuadraticTo(points[0].X, points[0].Y, points[1].X, points[1].Y); break;
                        case "cubicBezTo" when points.Length == 3: path.CubicTo(points[0].X, points[0].Y, points[1].X, points[1].Y, points[2].X, points[2].Y); break;
                        case "close": path.Close(); break;
                        default: throw new NotSupportedException("Unsupported DrawingML path command: " + command.Name.LocalName);
                    }
                }
                result.Append(path.Transform(Matrix3x2.CreateScale((float)(width / w), (float)(height / h))));
            }
            return result;
            double Value(string text)
            {
                if (values.TryGetValue(text, out double value)) { return value; }
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value)
                    ? value : throw new NotSupportedException("Unknown DrawingML guide: " + text);
            }
        }
    }
}
