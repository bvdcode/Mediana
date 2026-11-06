// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Globalization;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal class PresentationTheme
    {
        private readonly XElement? _elements;
        private readonly XElement? _map;
        internal PresentationTheme(XElement? theme, XElement? master, XElement slide)
        {
            _elements = theme?.Elements().FirstOrDefault(item => item.Name.LocalName == "themeElements");
            _map = slide.Elements().FirstOrDefault(item => item.Name.LocalName == "clrMapOvr")?.Elements().FirstOrDefault(item => item.Name.LocalName == "overrideClrMapping")
                ?? master?.Elements().FirstOrDefault(item => item.Name.LocalName == "clrMap");
        }
        internal string Font(string name)
        {
            string? family = name switch { "+mj-lt" => "majorFont", "+mn-lt" => "minorFont", _ => null };
            if (family is null) { return name; }
            return (string?)_elements?.Elements().FirstOrDefault(item => item.Name.LocalName == "fontScheme")?
                .Elements().FirstOrDefault(item => item.Name.LocalName == family)?
                .Elements().FirstOrDefault(item => item.Name.LocalName == "latin")?.Attribute("typeface")
                ?? throw new InvalidDataException("Presentation theme font is missing.");
        }
        internal XElement? Style(string kind, int index)
        {
            if (index <= 0) { return null; }
            string list = kind switch { "fillRef" => index >= 1001 ? "bgFillStyleLst" : "fillStyleLst", "lnRef" => "lnStyleLst", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
            int position = index >= 1001 ? index - 1001 : index - 1;
            return _elements?.Elements().FirstOrDefault(item => item.Name.LocalName == "fmtScheme")?
                .Elements().FirstOrDefault(item => item.Name.LocalName == list)?.Elements().ElementAtOrDefault(position);
        }
        internal Rgba32 Color(XElement color, Rgba32? placeholder = null, int depth = 0)
        {
            if (depth > 8) { throw new InvalidDataException("Cyclic presentation theme color."); }
            string? value = (string?)color.Attribute("val");
            Rgba32 result;
            switch (color.Name.LocalName)
            {
                case "srgbClr": result = Hex(value); break;
                case "scrgbClr": result = new(Linear("r"), Linear("g"), Linear("b")); break;
                case "hslClr": result = FromHsl(PresentationXml.Number(color, "hue") / 60000, PresentationXml.Number(color, "sat") / 100000, PresentationXml.Number(color, "lum") / 100000, 255); break;
                case "sysClr": result = Hex((string?)color.Attribute("lastClr") ?? throw new NotSupportedException("A system color requires a stored RGB value.")); break;
                case "schemeClr":
                    if (value == "phClr") { result = placeholder ?? throw new InvalidDataException("Missing theme placeholder color."); break; }
                    string key = value ?? throw new InvalidDataException("Missing theme color name.");
                    key = (string?)_map?.Attribute(key) ?? key switch { "bg1" => "lt1", "bg2" => "lt2", "tx1" => "dk1", "tx2" => "dk2", _ => key };
                    XElement? scheme = _elements?.Elements().FirstOrDefault(item => item.Name.LocalName == "clrScheme");
                    XElement entry = scheme?.Elements().FirstOrDefault(item => item.Name.LocalName == key)?.Elements().FirstOrDefault()
                        ?? throw new InvalidDataException("Missing presentation theme color: " + key);
                    result = Color(entry, placeholder, depth + 1); break;
                case "prstClr":
                    result = value switch
                    {
                        "black" => new(0, 0, 0),
                        "white" => new(255, 255, 255),
                        "red" => new(255, 0, 0),
                        "blue" => new(0, 0, 255),
                        "green" => new(0, 128, 0),
                        "yellow" => new(255, 255, 0),
                        "gray" => new(128, 128, 128),
                        _ => throw new NotSupportedException("Unsupported preset presentation color: " + value)
                    }; break;
                default: throw new NotSupportedException("Unsupported presentation color: " + color.Name.LocalName);
            }
            foreach (XElement adjustment in color.Elements())
            {
                double factor = PresentationXml.Number(adjustment, "val") / 100000;
                result = adjustment.Name.LocalName switch
                {
                    "alpha" => result with { A = Byte(factor * 255) },
                    "alphaMod" => result with { A = Byte(result.A * factor) },
                    "alphaOff" => result with { A = Byte(result.A + factor * 255) },
                    "tint" => Map(result, x => x * factor + 255 * (1 - factor)),
                    "shade" => Map(result, x => x * factor),
                    "hue" => Hsl(result, hue: PresentationXml.Number(adjustment, "val") / 60000),
                    "hueMod" => Hsl(result, hueMultiplier: factor),
                    "hueOff" => Hsl(result, hueOffset: PresentationXml.Number(adjustment, "val") / 60000),
                    "sat" => Hsl(result, saturation: factor),
                    "satMod" => Hsl(result, saturationMultiplier: factor),
                    "satOff" => Hsl(result, saturationOffset: factor),
                    "lum" => Hsl(result, luminance: factor),
                    "lumMod" => Hsl(result, luminanceMultiplier: factor),
                    "lumOff" => Hsl(result, luminanceOffset: factor),
                    "comp" => Hsl(result, hueOffset: 180),
                    "inv" => Map(result, x => 255 - x),
                    "redMod" => result with { R = Byte(result.R * factor) },
                    "greenMod" => result with { G = Byte(result.G * factor) },
                    "blueMod" => result with { B = Byte(result.B * factor) },
                    _ => throw new NotSupportedException("Unsupported presentation color transform: " + adjustment.Name.LocalName)
                };
            }
            return result;
            byte Linear(string channel)
            {
                double c = Math.Clamp(PresentationXml.Number(color, channel) / 100000, 0, 1);
                return Byte((c <= .0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - .055) * 255);
            }
        }
        private static Rgba32 Hsl(Rgba32 c, double? hue = null, double? saturation = null, double? luminance = null,
            double hueMultiplier = 1, double hueOffset = 0, double saturationMultiplier = 1, double saturationOffset = 0, double luminanceMultiplier = 1, double luminanceOffset = 0)
        {
            double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d;
            double high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b)), l = (high + low) / 2, delta = high - low;
            double s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
            double h = delta == 0 ? 0 : (high == r ? ((g - b) / delta + 6) % 6 : high == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60;
            return FromHsl(hue ?? h * hueMultiplier + hueOffset, saturation ?? s * saturationMultiplier + saturationOffset, luminance ?? l * luminanceMultiplier + luminanceOffset, c.A);
        }
        private static Rgba32 FromHsl(double hue, double saturation, double luminance, byte alpha)
        {
            double h = ((hue % 360 + 360) % 360) / 60, s = Math.Clamp(saturation, 0, 1), l = Math.Clamp(luminance, 0, 1);
            double chroma = (1 - Math.Abs(2 * l - 1)) * s, x = chroma * (1 - Math.Abs(h % 2 - 1)), m = l - chroma / 2;
            (double R, double G, double B) rgb = h switch { < 1 => (chroma, x, 0), < 2 => (x, chroma, 0), < 3 => (0, chroma, x), < 4 => (0, x, chroma), < 5 => (x, 0, chroma), _ => (chroma, 0, x) };
            return new(Byte((rgb.R + m) * 255), Byte((rgb.G + m) * 255), Byte((rgb.B + m) * 255), alpha);
        }
        private static Rgba32 Map(Rgba32 c, Func<double, double> f) => new(Byte(f(c.R)), Byte(f(c.G)), Byte(f(c.B)), c.A);
        private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
        private static Rgba32 Hex(string? value) => value is { Length: 6 } && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : throw new InvalidDataException("Invalid presentation RGB color.");
    }
}
