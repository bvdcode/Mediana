// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class TileComponent
    {
        internal Jpeg2000Component Output { get; init; } = null!;
        internal CodingStyle Coding { get; init; } = null!;
        internal int RoiShift { get; init; }
        internal Resolution[] Resolutions { get; init; } = null!;
        internal double[] Reconstructed { get; set; } = Array.Empty<double>();
    }
}
