// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordGlyphPaint(VectorPath Path, double X, double Baseline, double Scale, Rgba32 Color) : WordPaint;
}
