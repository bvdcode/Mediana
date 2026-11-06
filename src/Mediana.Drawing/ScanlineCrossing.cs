// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Mediana.Drawing
{
    internal readonly record struct ScanlineCrossing(double X, int Winding)
    {
        internal double Slope { get; init; }
        internal float OriginX { get; init; }
        internal float OriginY { get; init; }
        internal int Order { get; init; }
    }
}
