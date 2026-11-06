// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
namespace Mediana.Documents.Presentation
{
    internal record PresentationTextAtom(TrueTypeFont? Font,int Glyph,InlineGraphic? Graphic,double Size,Rgba32 Color,
        double Advance,double Ascent,double Descent,bool Space = false,bool Break = false);
}
