// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mediana.Drawing
{
    /// <summary>The rule used to determine the inside of a path.</summary>
    public enum FillRule
    {
        /// <summary>Use the signed number of edge crossings.</summary>
        NonZero,
        /// <summary>Alternate between outside and inside at each edge crossing.</summary>
        EvenOdd
    }
}
