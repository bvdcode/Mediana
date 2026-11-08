// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1MotionPrecision
    {
        internal static int Lower(int value, bool integer, bool highPrecision)
        {
            if (integer)
            {
                int rounded = (Math.Abs(value) + 3) >> 3;
                return value > 0 ? rounded << 3 : -(rounded << 3);
            }
            if (highPrecision) { return value; }
            return value > 0 ? value & ~1 : -((-value) & ~1);
        }
    }
}
