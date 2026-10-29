// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal class Tile
    {
        internal TileComponent[] Components { get; init; } = null!;
        internal HeaderSettings Settings { get; init; } = null!;
        internal Packet[] Packets { get; init; } = null!;
        internal int PacketIndex { get; set; }
        internal int NextPart { get; set; }
    }
}
