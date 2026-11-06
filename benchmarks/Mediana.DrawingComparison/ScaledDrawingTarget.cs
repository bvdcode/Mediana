// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.DrawingComparison
{
    internal class ScaledDrawingTarget(IDrawingTarget target, int scale) : IDrawingTarget
    {
        public void Clear(Rgba32 color) => target.Clear(color);
        public void Fill(ComparisonPath path, Rgba32 color, FillRule rule) => target.Fill(Scale(path), color, rule);
        public void Stroke(ComparisonPath path, Rgba32 color, float width) => target.Stroke(Scale(path), color, width * scale);
        public void Text(ReadOnlySpan<char> text, float x, float baseline, float size, Rgba32 color) => target.Text(text, x * scale, baseline * scale, size * scale, color);
        public float Measure(ReadOnlySpan<char> text, float size) => target.Measure(text, size * scale) / scale;
        private ComparisonPath Scale(ComparisonPath source)
        {
            ComparisonPath result = new();
            foreach (PathCommand command in source.Commands)
            {
                result.Commands.Add(command with { First = command.First * scale, Second = command.Second * scale, Third = command.Third * scale });
            }
            return result;
        }
    }
}
