// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class Resolution
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal List<Subband> Bands { get; } = new();
        internal Precinct[] Precincts { get; set; } = Array.Empty<Precinct>();
    }
}
