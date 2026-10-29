// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;

namespace Mediana.Images.Svg
{
    /// <summary>A requested CSS font face.</summary>
    public record SvgFontRequest(string Family, int Weight = 400, bool Italic = false);
}
