// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.DrawingBenchmarks
{
    internal partial class DrawingWorkload
    {
        private void Table(RasterCanvas canvas, bool filled, bool measureOnly)
        {
            const int columns = 6, rows = 14;
            const string text = "A long description — Длинное описание позиции, reference AV0123456789 ";
            TextRenderer renderer = new(new() { FontProgramResolver = _ => font });
            TrueTypeFont face = renderer.ResolveFont(new("Document"))!;
            float cellWidth = width / (float)columns, cellHeight = height / (float)rows;
            float size = Math.Max(5, cellHeight * .38f), units = size / face.Units;
            if (!measureOnly) { canvas.Buffer.Clear(new(250, 250, 250)); }
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x = cellWidth * column, y = cellHeight * row;
                    if (!measureOnly)
                    {
                        canvas.StrokePath(new VectorPath().Rectangle(x, y, cellWidth, cellHeight), new(70, 80, 90), new() { Width = .5f }, transform);
                    }
                    if (!filled && row != 0 && column != 0) { continue; }
                    ReadOnlySpan<char> label = text;
                    while (Measure(label, face) * units > cellWidth - 4 && label.Length > 0) { label = label[..^1]; }
                    if (measureOnly) { continue; }
                    int previous = 0;
                    x += 2;
                    foreach (char character in label)
                    {
                        int glyph = face.Glyph(character);
                        x += face.Kerning(previous, glyph) * units;
                        renderer.DrawGlyph(canvas, face, glyph, new(30, 40, 60),
                            Matrix3x2.CreateScale(units, -units) * Matrix3x2.CreateTranslation(x, y + size + 2) * transform);
                        x += face.Advance(glyph) * units;
                        previous = glyph;
                    }
                }
            }
        }

        private static float Measure(ReadOnlySpan<char> text, TrueTypeFont face)
        {
            float advance = 0;
            int previous = 0;
            foreach (char character in text)
            {
                int glyph = face.Glyph(character);
                advance += face.Advance(glyph) + face.Kerning(previous, glyph);
                previous = glyph;
            }
            return advance;
        }
    }
}
