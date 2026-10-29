// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Drawing;
namespace Mediana.Documents.Pdf
{
    internal record PdfGraphicsState
    {
        internal Matrix3x2 Transform { get; init; } = Matrix3x2.Identity;
        internal Rgba32 Fill { get; init; } = new(0, 0, 0);
        internal Rgba32 Stroke { get; init; } = new(0, 0, 0);
        internal float FillAlpha { get; init; } = 1;
        internal float StrokeAlpha { get; init; } = 1;
        internal ColorBlendMode Blend { get; init; }
        internal StrokeStyle Pen { get; init; } = new();
        internal PdfFont? Font { get; init; }
        internal float FontSize { get; init; }
        internal float CharacterSpace { get; init; }
        internal float WordSpace { get; init; }
        internal float HorizontalScale { get; init; } = 1;
        internal float Leading { get; init; }
        internal float Rise { get; init; }
        internal int TextMode { get; init; }
    }
}
