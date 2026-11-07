// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltSpectralOrder
    {
        public static void Haar(Span<float> vector, int width, int stride)
        {
            for (int column = 0; column < stride; column++)
            {
                for (int pair = 0; pair < width / 2; pair++)
                {
                    int at = 2 * pair * stride + column;
                    float left = .70710678f * vector[at];
                    float right = .70710678f * vector[at + stride];
                    vector[at] = left + right;
                    vector[at + stride] = left - right;
                }
            }
        }

        public static void Deinterleave(Span<float> vector, int width, int stride, bool hadamard) =>
            Reorder(vector, width, stride, hadamard, false);

        public static void Interleave(Span<float> vector, int width, int stride, bool hadamard) =>
            Reorder(vector, width, stride, hadamard, true);

        private static void Reorder(Span<float> vector, int width, int stride, bool hadamard, bool inverse)
        {
            Span<float> copy = stackalloc float[CeltBandTables.MaximumBandDimensions];
            vector.CopyTo(copy);
            for (int column = 0; column < stride; column++)
            {
                int row = column;
                if (hadamard) { row = Order[stride - 2 + column]; }
                for (int sample = 0; sample < width; sample++)
                {
                    int interleaved = sample * stride + column, grouped = row * width + sample;
                    if (inverse) { vector[interleaved] = copy[grouped]; }
                    else { vector[grouped] = copy[interleaved]; }
                }
            }
        }

        private static ReadOnlySpan<byte> Order =>
            [1, 0, 3, 0, 2, 1, 7, 0, 4, 3, 6, 1, 5, 2, 15, 0, 8, 7, 12, 3, 11, 4, 14, 1, 9, 6, 13, 2, 10, 5];
    }
}
