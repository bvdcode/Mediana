// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Text;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.DrawingComparison
{
    internal class MedianaTarget : IDrawingTarget
    {
        private readonly RasterCanvas canvas;
        private readonly TextRenderer text;
        private readonly TrueTypeFont face;
        internal MedianaTarget(RasterCanvas canvas, FontProgram program)
        {
            this.canvas = canvas;
            text = new(new() { FontProgramResolver = _ => program });
            face = text.ResolveFont(new("Input"))!;
        }
        public void PushClip(ComparisonPath path) { canvas.Save(); canvas.ClipPath(Convert(path), Matrix3x2.Identity); }
        public void PopClip() => canvas.Restore();
        public void Clear(Rgba32 color) => canvas.Buffer.Clear(color);
        public void Fill(ComparisonPath path, Rgba32 color, FillRule rule) => canvas.FillPathAntialiased(Convert(path), color, Matrix3x2.Identity, rule);
        public void Stroke(ComparisonPath path, Rgba32 color, float width) => canvas.StrokePathAntialiased(Convert(path), color, new() { Width = width }, Matrix3x2.Identity);

        public void Text(ReadOnlySpan<char> value, float x, float baseline, float size, Rgba32 color)
        {
            float units = size / face.Units;
            int previous = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                int glyph = face.Glyph(rune.Value);
                x += face.Kerning(previous, glyph) * units;
                text.DrawGlyphAntialiased(canvas, face, glyph, color,
                    Matrix3x2.CreateScale(units, -units) * Matrix3x2.CreateTranslation(x, baseline));
                x += face.Advance(glyph) * units;
                previous = glyph;
            }
        }

        public float Measure(ReadOnlySpan<char> value, float size)
        {
            float advance = 0;
            int previous = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                int glyph = face.Glyph(rune.Value);
                advance += face.Advance(glyph) + face.Kerning(previous, glyph);
                previous = glyph;
            }
            return advance * size / face.Units;
        }

        private static VectorPath Convert(ComparisonPath path)
        {
            VectorPath result = new();
            foreach (PathCommand command in path.Commands)
            {
                Vector2 a = command.First, b = command.Second, c = command.Third;
                switch (command.Verb)
                {
                    case PathVerb.Move: result.MoveTo(a.X, a.Y); break;
                    case PathVerb.Line: result.LineTo(a.X, a.Y); break;
                    case PathVerb.Quadratic: result.QuadraticTo(a.X, a.Y, b.X, b.Y); break;
                    case PathVerb.Cubic: result.CubicTo(a.X, a.Y, b.X, b.Y, c.X, c.Y); break;
                    case PathVerb.Close: result.Close(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(path));
                }
            }
            return result;
        }
    }
}
