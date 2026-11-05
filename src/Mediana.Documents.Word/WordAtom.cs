// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal readonly record struct WordAtom(WordRunFormat Format, VectorPath? Path, double Scale, double Advance,
        double Ascent, double Descent, bool Space = false, bool BreakAfter = false, WordPicture? Picture = null,
        bool Tab = false, bool LineBreak = false, bool PageBreak = false, int Codepoint = 0,
        InlineGraphic? Graphic = null, bool ClusterContinuation = false);
}
