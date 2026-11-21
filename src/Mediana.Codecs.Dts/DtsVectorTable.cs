// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static partial class DtsVectorTable
    {
        public static ReadOnlySpan<sbyte> Get(int index)
        {
            ReadOnlySpan<sbyte> page = (index / 128) switch
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
            return page.Slice((index % 128) * 32, 32);
        }
    }
}
