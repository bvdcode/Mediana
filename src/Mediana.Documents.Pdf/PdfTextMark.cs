// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    internal record PdfTextMark(int? Mcid,string? ActualText,bool Visible,bool Artifact,PdfValue Stream,Vector2 Position);
}
