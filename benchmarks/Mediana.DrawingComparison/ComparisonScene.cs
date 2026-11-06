// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.DrawingComparison
{
    internal partial class ComparisonScene(string name, int width, int height, int complexity, int scale = 1)
    {
        internal string Name => name;
        internal int Width => width;
        internal int Height => height;
        internal int Complexity => complexity;
        internal int Scale => scale;
        internal int PixelWidth => checked(width * scale);
        internal int PixelHeight => checked(height * scale);
        private static Rgba32 Ink => new(30, 40, 60);

        internal void Draw(IDrawingTarget target)
        {
            if (scale != 1) { target = new ScaledDrawingTarget(target, scale); }
            switch (name)
            {
                case "short-text": Text(target, false, false); break;
                case "dense-text": Text(target, true, false); break;
                case "mixed-text": Text(target, true, true); break;
                case "table-empty": Table(target, false); break;
                case "table-full": Table(target, true); break;
                case "bars": Bars(target); break;
                case "curves": Curves(target); break;
                case "clipped-curves":
                    target.PushClip(new ComparisonPath().Rectangle(width / 8, height / 8, width * 3 / 4, height * 3 / 4));
                    Curves(target); target.PopClip(); break;
                case "transformed-curves": Curves(target, true); break;
                case "strokes": Strokes(target); break;
                case "alpha": Tiles(target); break;
                default: throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        private void Text(IDrawingTarget target, bool dense, bool mixed)
        {
            target.Clear(new(250, 250, 250));
            string text = mixed ? "Пример текста, проверка 0123456789 — AVATAR office. " : "The quick brown fox, AVATAR office, 0123456789. ";
            float size = Math.Max(9, width / 100f), lineHeight = size * 1.5f;
            int characters = 0;
            for (float baseline = size * 1.5f; baseline < height - size; baseline += lineHeight)
            {
                string line = text;
                if (dense)
                {
                    int repetitions = Math.Max(1, (int)((width - size * 2) / (size * text.Length * .6f)));
                    line = string.Concat(Enumerable.Repeat(text, repetitions));
                }
                target.Text(line, size + (characters % 3) * .125f, baseline, size, Ink);
                characters++;
                if (!dense) { break; }
            }
        }

        private void Table(IDrawingTarget target, bool filled)
        {
            const int columns = 6, rows = 14;
            const string label = "AVATAR office: a long description, reference 0123456789";
            target.Clear(new(250, 250, 250));
            float cellWidth = width / (float)columns, cellHeight = height / (float)rows;
            float size = Math.Max(5, cellHeight * .38f);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x = cellWidth * column + .25f, y = cellHeight * row + .25f;
                    target.Stroke(new ComparisonPath().Rectangle(x, y, cellWidth, cellHeight), Ink, .5f);
                    if (!filled && row != 0 && column != 0) { continue; }
                    ReadOnlySpan<char> text = label;
                    while (text.Length > 0 && target.Measure(text, size) > cellWidth - 4) { text = text[..^1]; }
                    target.Text(text, x + 2, y + size + 2, size, Ink);
                }
            }
        }

        private void Bars(IDrawingTarget target)
        {
            int count = 20 * complexity;
            for (int i = 0; i < count; i++)
            {
                float barWidth = width * .6f / count, barHeight = height * (.08f + .85f * Math.Abs(MathF.Sin(i * .73f)));
                float x = width * .05f + i * width * .9f / count;
                target.Fill(ComparisonPath.Capsule(x, (height - barHeight) / 2, barWidth, barHeight), new(50, 210, 140));
            }
        }

        private void Curves(IDrawingTarget target, bool transformed = false)
        {
            int count = 30 * complexity;
            ComparisonPath path = new();
            for (int i = 0; i < count; i++)
            {
                float x = (i % 10) * width / 10f, y = (i / 10) * height / (float)Math.Ceiling(count / 10f);
                float w = width / 12f, h = height / (float)Math.Ceiling(count / 10f) * .4f;
                path.Move(x, y).Cubic(new(x + w, y - h), new(x - w, y + h), new(x + w, y + h))
                    .Quadratic(new(x, y + h * 2), new(x, y)).Close();
            }
            if (transformed)
            {
                System.Numerics.Matrix3x2 matrix = System.Numerics.Matrix3x2.CreateRotation(.3f, new(width / 2f, height / 2f));
                for (int i = 0; i < path.Commands.Count; i++)
                {
                    PathCommand command = path.Commands[i];
                    path.Commands[i] = command with
                    {
                        First = System.Numerics.Vector2.Transform(command.First, matrix),
                        Second = System.Numerics.Vector2.Transform(command.Second, matrix),
                        Third = System.Numerics.Vector2.Transform(command.Third, matrix)
                    };
                }
            }
            target.Fill(path, new(90, 50, 220, 160), FillRule.EvenOdd);
        }

        private void Strokes(IDrawingTarget target)
        {
            ComparisonPath path = new();
            int count = 20 * complexity;
            for (int i = 0; i < count; i++)
            {
                float y = height * (i + .5f) / count;
                path.Move(0, y).Cubic(new(width * .25f, y + height * .02f), new(width * .75f, y - height * .02f), new(width, y));
            }
            target.Stroke(path, new(150, 60, 40, 190), 1.2f);
        }

        private void Tiles(IDrawingTarget target)
        {
            int count = 80 * complexity;
            for (int i = 0; i < count; i++)
            {
                float x = (i % 20) * width / 20f + .3f, y = (i / 20) * height / (float)Math.Ceiling(count / 20f) + .2f;
                target.Fill(new ComparisonPath().Rectangle(x, y, width / 18f, height / (float)Math.Ceiling(count / 20f) * 1.1f),
                    new((byte)(i * 13), (byte)(i * 37), (byte)(i * 53), (byte)(60 + i % 196)));
            }
        }
    }
}
