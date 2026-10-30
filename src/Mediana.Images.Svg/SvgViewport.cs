// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal record SvgViewport(float Width, float Height, (float X, float Y, float Width, float Height) ViewBox)
    {
        internal static SvgViewport Read(XElement root)
        {
            if (root.Name.LocalName != "svg" || root.Name.NamespaceName is not ("" or "http://www.w3.org/2000/svg"))
            {
                throw new InvalidDataException("Expected an SVG root element.");
            }
            float[]? v = root.Attribute("viewBox") is { } box ? SvgNumbers.List(box.Value) : null;
            if (v is not null && (v.Length != 4 || v[2] <= 0 || v[3] <= 0))
            {
                throw new InvalidDataException("Invalid SVG viewBox.");
            }
            float width = root.Attribute("width") is { } w ? SvgLength.Parse(w.Value, 300) : v?[2] ?? 300;
            float height = root.Attribute("height") is { } h ? SvgLength.Parse(h.Value, 150) : v?[3] ?? 150;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            {
                throw new InvalidDataException("SVG viewport must be finite and positive.");
            }
            return new SvgViewport(width, height, v is null ? (0, 0, width, height) : (v[0], v[1], v[2], v[3]));
        }
    }
}
