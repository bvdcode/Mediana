// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal delegate void CoverageRow(int y, int left, ReadOnlySpan<float> coverage);
}
