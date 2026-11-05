// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Word
{
    /// <summary>Physical page dimensions in typographic points (1/72 inch).</summary>
    public record WordPageSize(double Width, double Height)
    {
        public static WordPageSize A4 { get; } = new(210 * 72 / 25.4, 297 * 72 / 25.4);
        public static WordPageSize Letter { get; } = new(612, 792);
    }
}
