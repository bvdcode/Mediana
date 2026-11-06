// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.DrawingBenchmarks
{
    internal class DrawingWorkload(string scene, int width, int height, int scale)
    {
        private readonly FontProgram font = BundledFontPrograms.GetProgram("Canada1500");
        private readonly Matrix3x2 transform = Matrix3x2.CreateScale(scale);

        internal void Draw(RasterCanvas canvas)
        {
            switch (scene)
            {
                case "text": Text(canvas); break;
                case "bars": Bars(canvas); break;
                case "curves": Curves(canvas); break;
                case "strokes": Strokes(canvas); break;
                case "tiles": Tiles(canvas); break;
                default: throw new ArgumentOutOfRangeException(nameof(scene));
            }
        }

        private void Text(RasterCanvas canvas)
        {
            canvas.Buffer.Clear(new(250, 250, 250));
            TextRenderer renderer = new(new() { FontProgramResolver = _ => font });
            TrueTypeFont face = renderer.ResolveFont(new("Document"))!;
            string text = "The quick brown fox — Пример текста 0123456789. ";
            float size = Math.Max(9, width / 100f);
            int character = 0;
            for (float y = size * 1.5f; y < height - size; y += size * 1.4f)
            {
                float x = size;
                while (x < width - size * 2)
                {
                    int glyph = face.Glyph(text[character++ % text.Length]);
                    Matrix3x2 placement = Matrix3x2.CreateScale(size / face.Units, -size / face.Units)
                        * Matrix3x2.CreateTranslation(x, y) * transform;
                    renderer.DrawGlyph(canvas, face, glyph, new(30, 40, 60), placement);
                    x += face.Advance(glyph) * size / face.Units;
                }
            }
        }

        private void Bars(RasterCanvas canvas)
        {
            for (int i = 0; i < 24; i++)
            {
                float w = width / 38f;
                float h = height * (0.1f + 0.75f * Math.Abs(MathF.Sin(i * 0.73f)));
                float x = width * .05f + i * width * .9f / 24;
                VectorPath path = new VectorPath().Rectangle(x, (height - h) / 2, w, h).RoundCorners(w / 2);
                canvas.FillPath(path, new(50, 210, 140), transform);
            }
        }

        private void Curves(RasterCanvas canvas)
        {
            VectorPath path = new();
            for (int i = 0; i < 120; i++)
            {
                float x = (i % 12) * width / 12f, y = (i / 12) * height / 10f;
                float w = width / 14f, h = height / 12f;
                path.MoveTo(x, y).CubicTo(x + w, y - h, x - w, y + h, x + w, y + h)
                    .QuadraticTo(x, y + h * 2, x, y).Close();
            }
            canvas.FillPath(path, new(90, 50, 220, 160), transform, FillRule.EvenOdd);
        }

        private void Strokes(RasterCanvas canvas)
        {
            VectorPath path = new();
            for (int i = 0; i < 80; i++)
            {
                float y = height * (i + .5f) / 80;
                path.MoveTo(0, y).CubicTo(width * .25f, y + height * .02f, width * .75f, y - height * .02f, width, y);
            }
            canvas.StrokePathAntialiased(path, new(150, 60, 40, 190), new() { Width = 1.2f }, transform);
        }

        private void Tiles(RasterCanvas canvas)
        {
            for (int i = 0; i < 360; i++)
            {
                float x = (i % 20) * width / 20f, y = (i / 20) * height / 18f;
                VectorPath path = new VectorPath().Rectangle(x, y, width / 22f, height / 20f);
                canvas.FillPath(path, new((byte)(i * 13), (byte)(i * 37), (byte)(i * 53), (byte)(100 + i % 156)), transform);
            }
        }
    }
}
