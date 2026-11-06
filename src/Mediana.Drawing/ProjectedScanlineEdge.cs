// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal readonly record struct ProjectedScanlineEdge(ScanlineEdge Edge, double MinimumX, double MaximumX);
}
