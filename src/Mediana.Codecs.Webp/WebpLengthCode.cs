// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Webp
{
    internal readonly record struct WebpLengthCode(int Symbol, uint Extra, int ExtraBits)
    {
        internal static WebpLengthCode FromValue(int value)
        {
            if (value <= 4) { return new(value - 1, 0, 0); }
            int remainder = value - 1;
            int highest = BitOperations.Log2((uint)remainder);
            int bits = highest - 1;
            int symbol = highest * 2 + ((remainder >> bits) & 1);
            return new(symbol, (uint)(remainder & ((1 << bits) - 1)), bits);
        }
    }
}
