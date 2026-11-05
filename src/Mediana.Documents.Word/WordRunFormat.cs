// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    internal record WordRunFormat
    {
        internal string Family { get; init; } = "Times New Roman";
        internal double Size { get; init; } = 12;
        internal bool Bold { get; init; }
        internal bool Italic { get; init; }
        internal bool Underline { get; init; }
        internal bool Strike { get; init; }
        internal bool SmallCaps { get; init; }
        internal bool Hidden { get; init; }
        internal double Baseline { get; init; }
        internal Rgba32 Color { get; init; } = new(0, 0, 0);
        internal Rgba32? Shading { get; init; }
    }
}
