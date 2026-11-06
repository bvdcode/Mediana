// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    internal record PdfTextState
    {
        internal Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
        internal PdfValue Font { get; set; } = PdfValue.Null;
        internal double Size { get; set; }
        internal double CharacterSpace { get; set; }
        internal double WordSpace { get; set; }
        internal double Horizontal { get; set; } = 1;
        internal double Leading { get; set; }
        internal double Rise { get; set; }
        internal int Mode { get; set; }
    }
}
