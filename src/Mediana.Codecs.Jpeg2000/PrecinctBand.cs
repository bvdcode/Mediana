// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class PrecinctBand
    {
        internal Subband Band { get; }
        internal List<CodeBlock> Blocks { get; } = new();
        internal TagTree Inclusion { get; set; } = null!;
        internal TagTree ZeroPlanes { get; set; } = null!;

        internal PrecinctBand(Subband band) => Band = band;
    }
}
