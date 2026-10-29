// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Images.Heif.Hevc
{
    internal static class IntegerTransform
    {
        private static readonly int[] CosineMagnitudes =
        {
            90, 90, 90, 90, 89, 88, 87, 85, 83, 82, 80, 78, 75, 73, 70, 67,
            64, 61, 57, 54, 50, 46, 43, 38, 36, 31, 25, 22, 18, 13, 9, 4, 0
        };

        private static readonly int[] Cosine8 = CreateCosine(8);
        private static readonly int[] Cosine16 = CreateCosine(16);
        private static readonly int[] Cosine32 = CreateCosine(32);

        /// <summary>
        /// Applies the HEVC inverse integer transform to one square coefficient block.
        /// </summary>
        /// <param name="coefficients">Row-major signed 16-bit coefficients; index = y * size + x.</param>
        /// <param name="residual">Worker-owned destination of at least size * size integers.</param>
        /// <param name="size">The transform width and height: 4, 8, 16 or 32.</param>
        /// <param name="bitDepth">The reconstructed sample depth: 8 or 10.</param>
        /// <param name="intraLuma">Whether a four-sample luma block requires the inverse sine transform.</param>
        /// <param name="workspace">Scratch storage owned exclusively by the current worker.</param>
        /// <remarks>
        /// The first, vertical pass adds 64, shifts arithmetically by seven bits and clips
        /// to signed 16-bit. The horizontal pass adds half the divisor and shifts by
        /// 20 - bitDepth. No input or output includes picture padding. Accumulators are
        /// bounded by 32 * 90 * 32768, so signed 32-bit lanes cannot overflow.
        /// Input and destination may overlap because the first pass reads all coefficients.
        /// </remarks>
        public static void Inverse(ReadOnlySpan<int> coefficients, Span<int> residual, int size,
            int bitDepth, bool intraLuma, TransformWorkspace workspace)
        {
            if (((size != 4) && (size != 8) && (size != 16) && (size != 32)) || ((bitDepth != 8) && (bitDepth != 10)))
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            int count = size * size;
            if ((coefficients.Length < count) || (residual.Length < count))
            {
                throw new ArgumentException("Transform buffers are shorter than the block.");
            }

            if (size == 4)
            {
                IntegerTransform4.Inverse(coefficients, residual, bitDepth, intraLuma, workspace);
                return;
            }

            (int activeWidth, int activeHeight) = ActiveExtent(coefficients, size);

            Span<int> intermediate = workspace.Intermediate.Slice(0, count);
            intermediate.Clear();
            ReadOnlySpan<int> matrix = Matrix(size);
            Span<int> column = stackalloc int[32];
            Span<int> sums = stackalloc int[32];
            for (int x = 0; x < activeWidth; x++)
            {
                for (int frequency = 0; frequency < activeHeight; frequency++)
                {
                    column[frequency] = coefficients[(frequency * size) + x];
                }

                Multiply(column.Slice(0, activeHeight), sums.Slice(0, size), matrix, size);
                for (int y = 0; y < size; y++)
                {
                    intermediate[(y * size) + x] = Math.Max(short.MinValue, Math.Min(short.MaxValue, (sums[y] + 64) >> 7));
                }
            }

            int shift = 20 - bitDepth;
            int offset = 1 << (shift - 1);
            for (int y = 0; y < size; y++)
            {
                Multiply(intermediate.Slice(y * size, activeWidth), sums.Slice(0, size), matrix, size);
                for (int x = 0; x < size; x++)
                {
                    residual[(y * size) + x] = (sums[x] + offset) >> shift;
                }
            }
        }

        private static void Multiply(ReadOnlySpan<int> input, Span<int> sums, ReadOnlySpan<int> matrix, int size)
        {
            sums.Clear();
            for (int frequency = 0; frequency < input.Length; frequency++)
            {
                int value = input[frequency];
                if (value == 0)
                {
                    continue;
                }

                ReadOnlySpan<int> basis = matrix.Slice(frequency * size, size);
                Vector<int> factor = new Vector<int>(value);
                Span<Vector<int>> vectorSums = MemoryMarshal.Cast<int, Vector<int>>(sums);
                ReadOnlySpan<Vector<int>> vectorBasis = MemoryMarshal.Cast<int, Vector<int>>(basis);
                for (int vector = 0; vector < vectorSums.Length; vector++)
                {
                    vectorSums[vector] += vectorBasis[vector] * factor;
                }

                for (int position = vectorSums.Length * Vector<int>.Count; position < size; position++)
                {
                    sums[position] += basis[position] * value;
                }
            }
        }

        private static ReadOnlySpan<int> Matrix(int size)
        {
            switch (size)
            {
                case 8:
                    return Cosine8;
                case 16:
                    return Cosine16;
                case 32:
                    return Cosine32;
                default:
                    throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        private static int[] CreateCosine(int size)
        {
            int[] matrix = new int[size * size];
            for (int frequency = 0; frequency < size; frequency++)
            {
                for (int position = 0; position < size; position++)
                {
                    matrix[(frequency * size) + position] = Basis(frequency, position, size);
                }
            }

            return matrix;
        }

        private static int Basis(int frequency, int position, int size)
        {
            if (frequency == 0)
            {
                return 64;
            }
            int angle = (((2 * position) + 1) * frequency * (32 / size)) & 127;
            if (angle > 64)
            {
                angle = 128 - angle;
            }
            return angle > 32 ? -CosineMagnitudes[64 - angle] : CosineMagnitudes[angle];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int Width, int Height) ActiveExtent(ReadOnlySpan<int> coefficients, int size)
        {
            int activeWidth = 0;
            int activeHeight = 0;
            for (int i = 0; i < (size * size); i++)
            {
                int value = coefficients[i];
                if ((value < short.MinValue) || (value > short.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(coefficients));
                }

                if (value != 0)
                {
                    activeWidth = Math.Max(activeWidth, (i % size) + 1);
                    activeHeight = (i / size) + 1;
                }
            }
            return (activeWidth, activeHeight);
        }
    }
}
