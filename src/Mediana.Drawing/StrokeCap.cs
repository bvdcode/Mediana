// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;

namespace Mediana.Drawing
{
    /// <summary>The shape of an open stroke's end.</summary>
    public enum StrokeCap
    {
        /// <summary>End exactly at the endpoint.</summary>
        Butt,
        /// <summary>A semicircular end.</summary>
        Round,
        /// <summary>Extend by half the line width.</summary>
        Square
    }
}
