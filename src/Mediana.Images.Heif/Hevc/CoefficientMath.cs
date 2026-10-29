// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal static class CoefficientMath
    {
        private static readonly int[] LevelScale = { 40, 45, 51, 57, 64, 72 };

        /// <summary>
        /// Applies the normal-precision inverse quantization defined in HEVC section 8.6.3.
        /// </summary>
        /// <param name="coefficient">A signed 16-bit transform coefficient.</param>
        /// <param name="scalingFactor">A scaling-list factor between zero and 255.</param>
        /// <param name="qp">The quantization parameter, including the bit-depth offset.</param>
        /// <param name="log2Size">The base-two logarithm of a block size from 4 through 32.</param>
        /// <param name="bitDepth">The supported sample depth: 8 or 10 bits.</param>
        /// <returns>The rounded and clipped signed 16-bit coefficient.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An input is outside its supported range.</exception>
        /// <remarks>
        /// Multiplication uses a 64-bit accumulator. Rounding adds half the divisor before
        /// an arithmetic right shift, including for negative values. Intermediate products
        /// must not wrap modulo 2^32. Clipping occurs after the rounding shift.
        /// </remarks>
        public static int Dequantize(int coefficient, int scalingFactor, int qp, int log2Size, int bitDepth)
        {
            ValidateQuantizer(qp, log2Size, bitDepth);
            if ((scalingFactor < 0) || (scalingFactor > 255))
            {
                throw new ArgumentOutOfRangeException(nameof(scalingFactor));
            }
            long multiplier = scalingFactor * LevelScale[qp % 6] * (1L << (qp / 6));
            return Scale(coefficient, multiplier, (bitDepth + log2Size) - 5);
        }

        /// <summary>
        /// Scales a transform block in place, preparing shared quantization parameters once.
        /// </summary>
        public static void Dequantize(Span<int> coefficients, ScalingLists scaling, int matrixId,
            int qp, int log2Size, int bitDepth)
        {
            ValidateQuantizer(qp, log2Size, bitDepth);
            int size = 1 << log2Size;
            int count = size * size;
            if (coefficients.Length < count)
            {
                throw new ArgumentException("Coefficient buffer is shorter than the transform block.", nameof(coefficients));
            }
            int shift = (bitDepth + log2Size) - 5;
            long multiplier = LevelScale[qp % 6] * (1L << (qp / 6));
            if (scaling.IsFlat)
            {
                multiplier *= scaling.Factor(log2Size, matrixId, 0, 0);
                for (int i = 0; i < count; i++)
                {
                    coefficients[i] = Scale(coefficients[i], multiplier, shift);
                }
                return;
            }
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int index = (row * size) + column;
                    coefficients[index] = Scale(coefficients[index],
                        multiplier * scaling.Factor(log2Size, matrixId, column, row), shift);
                }
            }
        }

        private static void ValidateQuantizer(int qp, int log2Size, int bitDepth)
        {
            if (((bitDepth != 8) && (bitDepth != 10)) || (qp < 0) || (qp > (51 + (6 * (bitDepth - 8)))) || (log2Size < 2) || (log2Size > 5))
            {
                throw new ArgumentOutOfRangeException(nameof(qp));
            }
        }

        private static int Scale(int coefficient, long multiplier, int shift)
        {
            if ((coefficient < short.MinValue) || (coefficient > short.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(coefficient));
            }
            long scaled = ((coefficient * multiplier) + (1L << (shift - 1))) >> shift;
            return (int)Math.Max(short.MinValue, Math.Min(short.MaxValue, scaled));
        }

        public static int ClipSample(int value, int bitDepth) => Math.Max(0, Math.Min((1 << bitDepth) - 1, value));
    }
}
