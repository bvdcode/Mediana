// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordInlineGraphicPaint(InlineGraphic Graphic, double X, double Top, double Size) : WordPaint;
}
