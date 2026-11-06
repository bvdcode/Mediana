// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.DrawingComparison
{
    internal interface IDrawingTarget
    {
        void Clear(Rgba32 color);
        void Fill(ComparisonPath path, Rgba32 color, FillRule rule = FillRule.NonZero);
        void Stroke(ComparisonPath path, Rgba32 color, float width);
        void Text(ReadOnlySpan<char> text, float x, float baseline, float size, Rgba32 color);
        float Measure(ReadOnlySpan<char> text, float size);
    }
}
