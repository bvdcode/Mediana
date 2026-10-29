// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Mediana.Images.Svg
{
    internal readonly record struct FontPoint(Vector2 Position, bool OnCurve);
}
