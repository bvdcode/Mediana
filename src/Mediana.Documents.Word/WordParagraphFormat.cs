// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordParagraphFormat
    {
        internal WordAlignment Alignment { get; init; }
        internal double Left { get; init; }
        internal double Right { get; init; }
        internal double FirstLine { get; init; }
        internal double Before { get; init; }
        internal double After { get; init; }
        internal double Line { get; init; } = 1;
        internal WordLineRule LineRule { get; init; }
        internal bool PageBreakBefore { get; init; }
        internal bool KeepNext { get; init; }
        internal bool KeepLines { get; init; }
        internal bool WidowControl { get; init; } = true;
        internal Rgba32? Shading { get; init; }
        internal WordBorders Borders { get; init; } = new();
        internal IReadOnlyList<WordTabStop> Tabs { get; init; } = Array.Empty<WordTabStop>();
    }
}
