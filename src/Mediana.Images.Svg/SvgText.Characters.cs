// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal partial class SvgSceneBuilder
    {
        private void GatherText(XElement element, Dictionary<string, string> style, TextSpan? parent, float parentSize, Matrix3x2 transform, bool preserve, List<TextCharacter> text, int depth)
        {
            Count(depth);
            float size = TextLength(Value(style, "font-size", "16"), parentSize);
            if (size < 0 || size > 1_000_000)
            {
                throw new InvalidDataException("Invalid SVG font size.");
            }

            style = new Dictionary<string, string>(style)
            {
                ["font-size"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            TextFeatures(style);
            TextSpan? span = new TextSpan
            {
                Element = element,
                Style = style,
                Parent = parent,
                Size = size,
                Transform = transform,
                X = TextPositions(element, "x", _width, size),
                Y = TextPositions(element, "y", _height, size),
                Dx = TextPositions(element, "dx", _width, size),
                Dy = TextPositions(element, "dy", _height, size),
                Rotate = element.Attribute("rotate") is { } rotate ? SvgNumbers.List(rotate.Value) : Array.Empty<float>()
            };
            string space = (string?)element.Attribute(XNamespace.Xml + "space") ?? "";
            preserve = space == "preserve" || (space != "default" && preserve) || Value(style, "white-space", "normal").StartsWith("pre", StringComparison.Ordinal);
            GatherChildren(element, span, size, transform, preserve, text, depth);
        }

        private void GatherChildren(XElement element, TextSpan span, float size, Matrix3x2 transform, bool preserve, List<TextCharacter> text, int depth)
        {
            Dictionary<string, string> style = span.Style;
            foreach (XNode node in element.Nodes())
            {
                if (node is XText value)
                {
                    foreach (Rune rune in value.Value.EnumerateRunes())
                    {
                        if (text.Count >= _limits.MaximumPathCommands)
                        {
                            throw new InvalidDataException("SVG text exceeds MaximumPathCommands.");
                        }

                        text.Add(new TextCharacter(rune, span, preserve));
                    }
                }
                else if (node is XElement child)
                {
                    if (child.Name.LocalName is "tspan" or "textPath" or "a")
                    {
                        Dictionary<string, string> childStyle = Style(child, style);
                        GatherText(child, childStyle, span, size, SvgNumbers.Transform((string?)child.Attribute("transform")) * transform, preserve, text, depth + 1);
                    }
                    else if (!Metadata.Contains(child.Name.LocalName))
                    {
                        Warn("SVG text element: " + child.Name.LocalName);
                    }
                }
            }
        }

        private static List<TextCharacter> NormalizeText(List<TextCharacter> source)
        {
            var result = new List<TextCharacter>();
            bool space = true;
            foreach (TextCharacter item in source)
            {
                bool whitespace = item.Rune.Value is 9 or 10 or 13 or 32;
                if (item.Preserve)
                {
                    result.Add(whitespace ? item with { Rune = new Rune(' ') } : item);
                    space = whitespace;
                }
                else if (!whitespace || !space)
                {
                    result.Add(whitespace ? item with { Rune = new Rune(' ') } : item);
                    space = whitespace;
                }
            }

            if (result.Count > 0 && !result[^1].Preserve && result[^1].Rune.Value == 32)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private void TextFeatures(Dictionary<string, string> style)
        {
            foreach ((string, string) feature in new[]
            {
                ("direction", "ltr"),
                ("unicode-bidi", "normal"),
                ("writing-mode", "horizontal-tb"),
                ("font-feature-settings", "normal"),
                ("font-variant", "normal")
            }

            )
            {
                if (Value(style, feature.Item1, feature.Item2) != feature.Item2)
                {
                    Warn("SVG text property: " + feature.Item1);
                }
            }
        }

        private static void Decorate(VectorPath path, Dictionary<string, string> style, float advance, float size)
        {
            float thickness = Math.Max(size / 16, .5f);
            foreach (string name in Value(style, "text-decoration", "none").Split(' '))
            {
                float? y = name switch
                {
                    "underline" => size * .1f,
                    "overline" => -size * .8f,
                    "line-through" => -size * .3f,
                    _ => null
                };
                if (y.HasValue)
                {
                    path.Rectangle(0, y.Value, advance, thickness);
                }
            }
        }

        private static bool Position(TextSpan span, ref float x, ref float y, out float rotation)
        {
            var ancestors = new Stack<TextSpan>();
            for (TextSpan? current = span; current is not null; current = current.Parent)
            {
                ancestors.Push(current);
            }

            bool absolute = false;
            float dx = 0, dy = 0;
            rotation = 0;
            foreach (TextSpan current in ancestors)
            {
                int i = current.Index++;
                if (i < current.X.Length)
                {
                    x = current.X[i];
                    absolute = true;
                }

                if (i < current.Y.Length)
                {
                    y = current.Y[i];
                    absolute = true;
                }

                if (i < current.Dx.Length)
                {
                    dx = current.Dx[i];
                }

                if (i < current.Dy.Length)
                {
                    dy = current.Dy[i];
                }

                if (current.Rotate.Length > 0)
                {
                    rotation = current.Rotate[Math.Min(i, current.Rotate.Length - 1)];
                }
            }

            x += dx;
            y += dy;
            return absolute;
        }

    }
}
