// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mediana.Codecs.Jbig2
{
    internal record Segment(uint Number, int Type, uint Page, uint[] References, int Length)
    {
        internal ReadOnlyMemory<byte> Data { get; set; }
    }
}
