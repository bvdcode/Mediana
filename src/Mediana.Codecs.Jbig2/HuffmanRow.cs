// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jbig2
{
    internal record HuffmanRow(int Length, int Range, int Base, bool Lower = false);
}
