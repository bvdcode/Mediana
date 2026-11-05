// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

using Mediana.Drawing.Fonts;

namespace Mediana.Images.Svg
{
    internal partial class SvgSceneBuilder
    {
        private SceneNode[] TextNodes(XElement e, Dictionary<string, string> style, int depth)
        {
            var raw = new List<TextCharacter>();
            GatherText(e, style, null, 16, Matrix3x2.Identity, false, raw, depth);
            List<TextCharacter>? text = NormalizeText(raw);
            List<PlacedGlyph> glyphs = LayoutText(text);
            AdjustTextLength(e, glyphs);
            Anchor(glyphs);
            return BuildTextNodes(glyphs, depth);
        }

        private List<PlacedGlyph> LayoutText(List<TextCharacter> text)
        {
            var glyphs = new List<PlacedGlyph>();
            float x = 0, y = 0;
            TrueTypeFont? previousFont = null;
            int previousGlyph = 0;
            StringBuilder builder = new();
            foreach (TextCharacter item in text) { builder.Append(item.Rune.ToString()); }
            string source = builder.ToString();
            int offset = 0, clusterEnd = 0;
            for (int index = 0; index < text.Count; index++)
            {
                TextCharacter character = text[index];
                _token.ThrowIfCancellationRequested();
                InlineGraphic? graphic = null;
                if (offset == clusterEnd)
                {
                    int length = StringInfo.GetNextTextElementLength(source.AsSpan(offset));
                    clusterEnd = offset + length;
                    graphic = _text.ResolveGraphic(source.AsSpan(offset, length), _token);
                }
                bool chunk = Position(character.Span, ref x, ref y, out float rotation);
                if (graphic is not null)
                {
                    float size = character.Span.Size;
                    float graphicAdvance = (float)(graphic.Advance * size) + TextLength(Value(character.Span.Style, "letter-spacing", "0"), size);
                    VectorPath decoration = new();
                    Decorate(decoration, character.Span.Style, graphicAdvance, size);
                    glyphs.Add(new(decoration, character.Span.Style, x, y + GraphicBaseline(character.Span.Style, graphic, size), graphicAdvance, rotation, character.Span, chunk || glyphs.Count == 0)
                    {
                        Graphic = graphic,
                        GraphicTransform = Matrix3x2.CreateScale(size) * Matrix3x2.CreateTranslation(0, (float)(-graphic.Ascent * size))
                    });
                    x += graphicAdvance;
                    offset += character.Rune.Utf16SequenceLength;
                    while (offset < clusterEnd)
                    {
                        TextCharacter continuation = text[++index];
                        float ignoredX = x, ignoredY = y;
                        Position(continuation.Span, ref ignoredX, ref ignoredY, out _);
                        offset += continuation.Rune.Utf16SequenceLength;
                    }
                    previousFont = null;
                    continue;
                }
                offset += character.Rune.Utf16SequenceLength;
                if (character.Rune.Value is 0xFE0E or 0xFE0F or 0x200D or 0xAD) { continue; }
                (TrueTypeFont font, int glyph) = TextFont(character.Span.Style, character.Rune.Value);
                float scale = character.Span.Size / font.Units;
                if (!chunk && previousFont == font && Value(character.Span.Style, "font-kerning", "auto") != "none")
                {
                    x += font.Kerning(previousGlyph, glyph) * scale;
                }

                VectorPath path = _text.BorrowOutline(font, glyph).Transform(Matrix3x2.CreateScale(scale, -scale));
                float spacing = TextLength(Value(character.Span.Style, "letter-spacing", "0"), character.Span.Size);
                if (Rune.IsWhiteSpace(character.Rune))
                {
                    spacing += TextLength(Value(character.Span.Style, "word-spacing", "0"), character.Span.Size);
                }

                float advance = (font.Advance(glyph) * scale) + spacing;
                Decorate(path, character.Span.Style, advance, character.Span.Size);
                glyphs.Add(new PlacedGlyph(path, character.Span.Style, x, y + Baseline(character.Span.Style, font, character.Span.Size), advance, rotation, character.Span, chunk || glyphs.Count == 0));
                x += advance;
                previousFont = font;
                previousGlyph = glyph;
            }

            return glyphs;
        }

        private SceneNode[] BuildTextNodes(List<PlacedGlyph> glyphs, int depth)
        {
            var result = new List<SceneNode>();
            var spans = new Dictionary<TextSpan, (SceneNode Node, List<SceneNode> Children)>();
            List<SceneNode> Container(TextSpan span)
            {
                if (span.Parent is null)
                {
                    return result;
                }

                if (!spans.TryGetValue(span, out (SceneNode Node, List<SceneNode> Children) group))
                {
                    var children = new List<SceneNode>();
                    SceneNode node = Empty with
                    {
                        Opacity = Opacity(span.Style, "opacity"),
                        Clip = Clip(Value(span.Style, "clip-path", "none"), depth + 1),
                        Mask = Mask(Value(span.Style, "mask", "none"), depth + 1),
                        Filter = Filter(Value(span.Style, "filter", "none"), span.Style, depth + 1)
                    };
                    spans.Add(span, group = (node, children));
                    Container(span.Parent).Add(node);
                }

                return group.Children;
            }

            foreach (PlacedGlyph glyph in glyphs)
            {
                Matrix3x2? placement = TextGlyphPlacement(glyph);
                if (placement is null) { continue; }
                VectorPath path = glyph.Path.Transform(placement.Value);
                path = ValidatePath(path, glyph.Style) ?? new VectorPath();
                Container(glyph.Span).Add(new SceneNode(path, Paint(Value(glyph.Style, "fill", "black"), glyph.Style), Paint(Value(glyph.Style, "stroke", "none"), glyph.Style), Pen(glyph.Style), Rule(Value(glyph.Style, "fill-rule", "nonzero")), FillRule.NonZero, Matrix3x2.Identity, 1, Opacity(glyph.Style, "fill-opacity"), Opacity(glyph.Style, "stroke-opacity"), null, Array.Empty<SceneNode>())
                {
                    Graphic = Value(glyph.Style, "visibility", "visible") is "hidden" or "collapse" ? null : glyph.Graphic,
                    GraphicTransform = glyph.GraphicTransform * placement.Value
                });
            }

            SceneNode Finish(SceneNode node)
            {
                foreach ((SceneNode Node, List<SceneNode> Children) group in spans.Values)
                {
                    if (ReferenceEquals(group.Node, node))
                    {
                        return node with
                        {
                            Children = group.Children.Select(Finish).ToArray()
                        };
                    }
                }

                return node;
            }

            return result.Select(Finish).ToArray();
        }

        private static void Anchor(List<PlacedGlyph> glyphs)
        {
            int start = 0;
            while (start < glyphs.Count)
            {
                int end = start + 1;
                while (end < glyphs.Count && !glyphs[end].Chunk)
                {
                    end++;
                }

                float width = (glyphs[end - 1].X + glyphs[end - 1].Advance) - glyphs[start].X;
                float shift = Value(glyphs[start].Style, "text-anchor", "start") switch
                {
                    "middle" => width / 2,
                    "end" => width,
                    _ => 0
                };
                for (int i = start; i < end; i++)
                {
                    glyphs[i] = glyphs[i] with
                    {
                        X = glyphs[i].X - shift
                    };
                }

                start = end;
            }
        }

        private static void AdjustTextLength(XElement e, List<PlacedGlyph> glyphs)
        {
            if (e.Attribute("textLength") is not { } length || glyphs.Count == 0)
            {
                return;
            }

            float target = SvgLength.Parse(length.Value), actual = (glyphs[^1].X + glyphs[^1].Advance) - glyphs[0].X;
            if (target < 0 || actual <= 0)
            {
                return;
            }

            bool scale = (string?)e.Attribute("lengthAdjust") == "spacingAndGlyphs";
            float origin = glyphs[0].X;
            for (int i = 0; i < glyphs.Count; i++)
            {
                PlacedGlyph g = glyphs[i];
                glyphs[i] = scale ? g with
                {
                    X = origin + (((g.X - origin) * target) / actual),
                    Path = g.Path.Transform(Matrix3x2.CreateScale(target / actual, 1)),
                    Advance = (g.Advance * target) / actual,
                    GraphicTransform = g.GraphicTransform * Matrix3x2.CreateScale(target / actual, 1)
                }

                : g with
                {
                    X = g.X + (((target - actual) * i) / Math.Max(1, glyphs.Count - 1))
                };
            }
        }

        private Matrix3x2? TextGlyphPlacement(PlacedGlyph glyph)
        {
            TextSpan? path = glyph.Span;
            while (path is not null && path.Element.Name.LocalName != "textPath")
            {
                path = path.Parent;
            }

            Matrix3x2 placement = Matrix3x2.CreateRotation((glyph.Rotation * MathF.PI) / 180) * Matrix3x2.CreateTranslation(glyph.X, glyph.Y);
            if (path is not null)
            {
                XElement? target = Reference(Href(path.Element));
                VectorPath? curve = target is null ? null : Shape(target)?.Transform(SvgNumbers.Transform((string?)target.Attribute("transform")));
                if (curve is null)
                {
                    return null;
                }

                float offset = SvgLength.Parse((string?)path.Element.Attribute("startOffset") ?? "0", PathLength(curve));
                if (!PointOnPath(curve, glyph.X + offset + (glyph.Advance / 2), out Vector2 point, out float angle))
                {
                    return null;
                }

                placement = Matrix3x2.CreateTranslation(-glyph.Advance / 2, glyph.Y) * Matrix3x2.CreateRotation(angle + ((glyph.Rotation * MathF.PI) / 180)) * Matrix3x2.CreateTranslation(point);
            }

            return placement * glyph.Span.Transform;
        }

        private float PathLength(VectorPath path) => PathSegments(path).Sum(segment => segment.Length);
        private List<(Vector2 A, Vector2 B, float Length)> PathSegments(VectorPath path)
        {
            var result = new List<(Vector2, Vector2, float)>();
            foreach (PathContour contour in PathFlattener.Flatten(path, Matrix3x2.Identity, .05f, _token))
            {
                for (int i = 1; i < contour.Points.Count; i++)
                {
                    result.Add((contour.Points[i - 1], contour.Points[i], Vector2.Distance(contour.Points[i - 1], contour.Points[i])));
                }

                if (contour.Closed && contour.Points.Count > 1)
                {
                    result.Add((contour.Points[^1], contour.Points[0], Vector2.Distance(contour.Points[^1], contour.Points[0])));
                }
            }

            return result;
        }

        private bool PointOnPath(VectorPath path, float distance, out Vector2 point, out float angle)
        {
            foreach ((Vector2 A, Vector2 B, float Length) segment in PathSegments(path))
            {
                if (distance >= 0 && distance <= segment.Length && segment.Length > 0)
                {
                    point = Vector2.Lerp(segment.A, segment.B, distance / segment.Length);
                    angle = MathF.Atan2(segment.B.Y - segment.A.Y, segment.B.X - segment.A.X);
                    return true;
                }

                distance -= segment.Length;
            }

            point = default;
            angle = 0;
            return false;
        }
    }
}
