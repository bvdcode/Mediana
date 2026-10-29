// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Mediana.Drawing
{
    internal record PathContour(List<Vector2> Points, bool Closed);
}
