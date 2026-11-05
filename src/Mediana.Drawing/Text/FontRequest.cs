// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    /// <summary>A requested font family and face, independent of the source document format.</summary>
    public record FontRequest(string Family, bool Bold = false, bool Italic = false);
}
