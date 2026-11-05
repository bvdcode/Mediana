// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordRectanglePaint(double X, double Y, double Width, double Height, Rgba32 Color) : WordPaint;
}
