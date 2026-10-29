// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    /// <summary>One-based page number and visible dimensions in points (1/72 inch), after crop, rotation and UserUnit.</summary>
    public record PdfPageInfo(int Number, double Width, double Height, int Rotation);
}
