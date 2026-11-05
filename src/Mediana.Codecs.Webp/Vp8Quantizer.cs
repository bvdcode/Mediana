// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static class Vp8Quantizer
    {
        internal static int FromQuality(int quality)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(quality);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
            double normalized = quality / 100d;
            double compression = quality < 75 ? normalized * 2 / 3 : normalized * 2 - 1;
            return (int)Math.Round(127 * (1 - Math.Cbrt(compression)), MidpointRounding.AwayFromZero);
        }
    }
}
