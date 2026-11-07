// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltVectorMath
    {
        public static void Normalize(Span<float> vector, float gain)
        {
            float energy = 0;
            foreach (float coefficient in vector) { energy += coefficient * coefficient; }
            float scale = (1f / MathF.Sqrt(energy + 1e-15f)) * gain;
            for (int at = 0; at < vector.Length; at++) { vector[at] *= scale; }
        }
    }
}
