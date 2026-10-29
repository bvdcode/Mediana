// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class Precinct
    {
        internal long X { get; init; }
        internal long Y { get; init; }
        internal List<PrecinctBand> Bands { get; } = new();
    }
}
