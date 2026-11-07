// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltEnergyMath
    {
        /// <summary>CELT's floating energy reconstruction discards base-two exponents below -50.</summary>
        public static float Exp2(float exponent)
        {
            if (exponent < -50) { return 0; }
            return MathF.Pow(2, exponent);
        }
    }
}
