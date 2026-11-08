// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1Transform4
    {
        internal static void Inverse(Span<int> values, int type) => Av1InverseTransform.Inverse(values, 4, 4, type);
    }
}
