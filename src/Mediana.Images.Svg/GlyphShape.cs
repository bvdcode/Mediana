// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Mediana.Images.Svg
{
    internal record GlyphShape(List<FontPoint> Points, List<int> Ends);
}
