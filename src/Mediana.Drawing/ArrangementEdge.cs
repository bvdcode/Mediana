// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Mediana.Drawing
{
    internal record ArrangementEdge(Vector2 A, Vector2 B, int Operand)
    {
        internal List<double> Splits { get; } = new()
        {
            0,
            1
        };
        internal float Left => Math.Min(A.X, B.X);
        internal float Right => Math.Max(A.X, B.X);
        internal float Top => Math.Min(A.Y, B.Y);
        internal float Bottom => Math.Max(A.Y, B.Y);
    }
}
