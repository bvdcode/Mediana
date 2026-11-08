// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1WarpSample(int Row, int Column, int DestinationRow, int DestinationColumn);
}
