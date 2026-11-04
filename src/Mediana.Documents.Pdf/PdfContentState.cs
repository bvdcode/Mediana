// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal readonly record struct PdfContentState(PdfGraphicsState Graphics, VectorPath Path, Vector2 Current,
        Vector2 Start, bool HasPoint, FillRule? PendingClip, Matrix3x2 Text, Matrix3x2 TextLine,
        bool InText, VectorPath TextClip, bool HasTextClip);
}
