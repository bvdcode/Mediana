// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mediana.Codecs.Jbig2
{
    internal record SymbolDictionary(Bitmap[] Symbols, byte[] Generic, byte[] Refinement, int Flags, (int, int)[] At, (int, int)[] RefAt);
}
