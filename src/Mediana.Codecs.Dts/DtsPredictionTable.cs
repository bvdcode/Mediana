// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsPredictionTable
    {
        public static ReadOnlySpan<short> Get(int index)
        {
            ReadOnlySpan<short> page = (index / 512) switch
            {
                0 => Page0,
                1 => Page1,
                2 => Page2,
                3 => Page3,
                4 => Page4,
                5 => Page5,
                6 => Page6,
                7 => Page7,
                _ => throw new InvalidDataException("The DTS vector index is invalid.")
            };
            return page.Slice((index % 512) * 4, 4);
        }
    }
}
