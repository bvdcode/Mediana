// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Mediana.Drawing;
using System.Globalization;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordFormatReader(WordReadContext context)
    {
        private XNamespace W => context.W;

        internal WordRunFormat Run(WordRunFormat prior, XElement? properties, bool toggle)
        {
            if (properties is null) { return prior; }
            WordRunFormat result = prior;
            foreach (XElement item in properties.Elements())
            {
                if (item.Name.Namespace != W) { continue; }
                string? value = context.Value(item);
                bool Flag(bool current) => toggle ? current ^ WordReadContext.On(item) : WordReadContext.On(item);
                result = item.Name.LocalName switch
                {
                    "b" => result with { Bold = Flag(result.Bold) },
                    "i" => result with { Italic = Flag(result.Italic) },
                    "strike" => result with { Strike = Flag(result.Strike) },
                    "smallCaps" => result with { SmallCaps = Flag(result.SmallCaps) },
                    "vanish" => result with { Hidden = Flag(result.Hidden) },
                    "rFonts" => result with { Family = context.Attribute(item, "ascii") ?? context.Attribute(item, "hAnsi") ?? result.Family },
                    "sz" => result with { Size = Positive(context.Length(value ?? throw new InvalidDataException("Missing font size."), OpenXmlNumericUnit.HalfPoint)) },
                    "color" => result with { Color = Color(value) ?? new Rgba32(0, 0, 0) },
                    "shd" => result with { Shading = Shading(item) },
                    "highlight" => result with { Shading = Highlight(value) },
                    "u" => result with { Underline = value switch
                    {
                        null or "single" => true,
                        "none" => false,
                        _ => throw new NotSupportedException("Unsupported Word underline style: " + value)
                    } },
                    "vertAlign" => value switch
                    {
                        "baseline" => result with { Baseline = 0 },
                        "superscript" => result with { Baseline = .35 },
                        "subscript" => result with { Baseline = -.2 },
                        _ => throw new InvalidDataException("Invalid Word vertical text alignment.")
                    },
                    "rtl" when WordReadContext.On(item) => throw new NotSupportedException("Right-to-left Word shaping is not supported."),
                    _ => result
                };
            }
            return result;
        }

        internal WordParagraphFormat Paragraph(XElement properties)
        {
            XElement? indentation = properties.Element(W + "ind"), spacing = properties.Element(W + "spacing");
            string? lineRule = context.Attribute(spacing, "lineRule");
            double line = lineRule is null or "auto" ? context.Length(spacing, "line", 12) / 12 : context.Length(spacing, "line");
            WordParagraphFormat format = new()
            {
                Alignment = Alignment(context.Value(properties.Element(W + "jc"))),
                Left = context.Length(indentation, "start", context.Length(indentation, "left")),
                Right = context.Length(indentation, "end", context.Length(indentation, "right")),
                FirstLine = context.Attribute(indentation, "hanging") is string hanging
                    ? -context.Length(hanging) : context.Length(indentation, "firstLine"),
                Before = context.Length(spacing, "before"),
                After = context.Length(spacing, "after"),
                Line = Positive(line),
                LineRule = lineRule switch
                {
                    null or "auto" => WordLineRule.Multiple,
                    "atLeast" => WordLineRule.AtLeast,
                    "exact" => WordLineRule.Exact,
                    _ => throw new InvalidDataException("Invalid Word line spacing rule.")
                },
                PageBreakBefore = WordReadContext.On(properties.Element(W + "pageBreakBefore")),
                KeepNext = WordReadContext.On(properties.Element(W + "keepNext")),
                KeepLines = WordReadContext.On(properties.Element(W + "keepLines")),
                WidowControl = properties.Element(W + "widowControl") is null || WordReadContext.On(properties.Element(W + "widowControl")),
                Shading = Shading(properties.Element(W + "shd")),
                Borders = Borders(properties.Element(W + "pBdr"))
            };
            if (WordReadContext.On(properties.Element(W + "bidi"))) { throw new NotSupportedException("Bidirectional Word paragraphs are not supported."); }
            return format;
        }

        internal static WordAlignment Alignment(string? value) => value switch
        {
            null or "left" or "start" => WordAlignment.Left,
            "center" => WordAlignment.Center,
            "right" or "end" => WordAlignment.Right,
            "both" => WordAlignment.Justify,
            _ => throw new NotSupportedException("Unsupported Word alignment: " + value)
        };

        internal WordBorders Borders(XElement? properties) => new(Border(properties?.Element(W + "left") ?? properties?.Element(W + "start")),
            Border(properties?.Element(W + "right") ?? properties?.Element(W + "end")), Border(properties?.Element(W + "top")), Border(properties?.Element(W + "bottom")));

        private WordBorder? Border(XElement? item)
        {
            if (item is null || context.Value(item) is "nil" or "none") { return null; }
            if (context.Value(item) != "single") { throw new NotSupportedException("Unsupported Word border style: " + context.Value(item)); }
            double width = context.Attribute(item, "sz") is string size ? WordReadContext.Integer(size) / 8.0 : .5;
            if (width < 0) { throw new InvalidDataException("Invalid Word border width."); }
            return new(width, Color(context.Attribute(item, "color")) ?? new Rgba32(0, 0, 0));
        }

        internal Rgba32? Shading(XElement? item)
        {
            if (item is null) { return null; }
            if (context.Value(item) is not (null or "clear" or "nil")) { throw new NotSupportedException("Patterned Word shading is not supported."); }
            return Color(context.Attribute(item, "fill"));
        }

        internal static Rgba32? Color(string? value)
        {
            if (value is null or "auto") { return null; }
            if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int color))
            {
                throw new InvalidDataException("Invalid Word RGB color.");
            }
            return new((byte)(color >> 16), (byte)(color >> 8), (byte)color);
        }

        private static Rgba32? Highlight(string? value) => value switch
        {
            "none" => null,
            "white" => new(255, 255, 255),
            "yellow" => new(255, 255, 0),
            "black" => new(0, 0, 0),
            "blue" => new(0, 0, 255),
            "cyan" => new(0, 255, 255),
            "green" => new(0, 255, 0),
            "magenta" => new(255, 0, 255),
            "red" => new(255, 0, 0),
            "darkBlue" => new(0, 0, 128),
            "darkCyan" => new(0, 128, 128),
            "darkGreen" => new(0, 128, 0),
            "darkMagenta" => new(128, 0, 128),
            "darkRed" => new(128, 0, 0),
            "darkYellow" => new(128, 128, 0),
            "darkGray" => new(128, 128, 128),
            "lightGray" => new(192, 192, 192),
            _ => throw new InvalidDataException("Invalid Word highlight color.")
        };

        internal static double Positive(double value) => value > 0 && double.IsFinite(value)
            ? value : throw new InvalidDataException("A positive finite Word measurement is required.");
    }
}
