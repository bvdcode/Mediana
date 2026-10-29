// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal record CodingStyle(int Flags, int Progression, int Layers, bool Mct, int Levels, int BlockX, int BlockY, int BlockStyle, bool Reversible, byte[] Precincts);
}
