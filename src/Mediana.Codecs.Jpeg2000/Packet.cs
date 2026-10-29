// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Jpeg2000
{
    internal readonly record struct Packet(int Layer, int Resolution, int Component, int Precinct, long X, long Y);
}
