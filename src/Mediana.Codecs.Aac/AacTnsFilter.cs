// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal record AacTnsFilter(int Start, int End, bool Backward, double[] Coefficients);
}
