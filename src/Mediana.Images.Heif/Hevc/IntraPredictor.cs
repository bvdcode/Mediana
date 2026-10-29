// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Produces planar, DC and angular intra prediction for Main and Main 10 blocks.
    /// </summary>
    /// <remarks>
    /// The destination is a tightly packed size by size block indexed by y * size + x.
    /// Borders use the layout documented by IntraReferenceSamples and must have been
    /// substituted. Input borders remain unchanged. Scratch storage belongs to this call.
    /// All interpolation rounds with a positive half-unit before an arithmetic right shift;
    /// intermediate products fit in signed 32 bits for the supported 8/10-bit samples.
    /// </remarks>
    internal static class IntraPredictor
    {
        private static readonly int[] Angles =
        {
            0, 0, 32, 26, 21, 17, 13, 9, 5, 2, 0, -2, -5, -9, -13, -17, -21, -26,
            -32, -26, -21, -17, -13, -9, -5, -2, 0, 2, 5, 9, 13, 17, 21, 26, 32
        };

        public static void Predict(ReadOnlySpan<int> top, ReadOnlySpan<int> left, Span<int> destination,
            int size, int mode, int bitDepth, bool luma, bool strongSmoothing, bool fullResolutionChroma = false)
        {
            IntraReferenceSamples.Validate(top, left, size, bitDepth);
            if ((mode < 0) || (mode > 34))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (destination.Length < (size * size))
            {
                throw new ArgumentException("Prediction destination is too short.", nameof(destination));
            }
            for (int i = 0; i <= (2 * size); i++)
            {
                if ((top[i] < 0) || (left[i] < 0))
                {
                    throw new ArgumentException("Intra reference substitution is required.");
                }
            }

            Span<int> filteredTop = stackalloc int[65];
            Span<int> filteredLeft = stackalloc int[65];
            top.Slice(0, (2 * size) + 1).CopyTo(filteredTop);
            left.Slice(0, (2 * size) + 1).CopyTo(filteredLeft);
            if (luma || fullResolutionChroma)
            {
                IntraReferenceFilter.Apply(filteredTop, filteredLeft, size, mode, bitDepth, strongSmoothing && luma);
            }
            switch (mode)
            {
                case 0:
                    Planar(filteredTop, filteredLeft, destination, size);
                    break;
                case 1:
                    Dc(filteredTop, filteredLeft, destination, size, luma);
                    break;
                default:
                    Angular(filteredTop, filteredLeft, destination, size, mode, bitDepth, luma);
                    break;
            }
        }

        private static void Planar(ReadOnlySpan<int> top, ReadOnlySpan<int> left, Span<int> output, int size)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int horizontal = ((size - 1 - x) * left[y + 1]) + ((x + 1) * top[size + 1]);
                    int vertical = ((size - 1 - y) * top[x + 1]) + ((y + 1) * left[size + 1]);
                    output[(y * size) + x] = (horizontal + vertical + size) / (2 * size);
                }
            }
        }

        private static void Dc(ReadOnlySpan<int> top, ReadOnlySpan<int> left, Span<int> output, int size, bool luma)
        {
            int sum = size;
            for (int i = 1; i <= size; i++)
            {
                sum += top[i] + left[i];
            }
            int dc = sum / (2 * size);
            output.Slice(0, size * size).Fill(dc);
            if (luma && (size < 32))
            {
                output[0] = (left[1] + (2 * dc) + top[1] + 2) >> 2;
                for (int i = 1; i < size; i++)
                {
                    output[i] = (top[i + 1] + (3 * dc) + 2) >> 2;
                    output[i * size] = (left[i + 1] + (3 * dc) + 2) >> 2;
                }
            }
        }

        private static void Angular(ReadOnlySpan<int> top, ReadOnlySpan<int> left, Span<int> output,
            int size, int mode, int bitDepth, bool luma)
        {
            bool vertical = mode >= 18;
            ReadOnlySpan<int> main = vertical ? top : left;
            ReadOnlySpan<int> side = vertical ? left : top;
            Span<int> reference = stackalloc int[97];
            const int origin = 32;
            main.Slice(0, (2 * size) + 1).CopyTo(reference.Slice(origin));
            int angle = Angles[mode];
            if (angle < 0)
            {
                int inverse = InverseAngle(-angle);
                for (int k = -1; k >= ((size * angle) >> 5); k--)
                {
                    reference[origin + k] = side[((-k * inverse) + 128) >> 8];
                }
            }

            for (int row = 0; row < size; row++)
            {
                int displacement = (row + 1) * angle;
                int index = displacement >> 5;
                int fraction = displacement & 31;
                for (int column = 0; column < size; column++)
                {
                    int first = reference[origin + column + index + 1];
                    int value = first;
                    if (fraction != 0)
                    {
                        value = (((32 - fraction) * first) + (fraction * reference[origin + column + index + 2]) + 16) >> 5;
                    }

                    output[vertical ? (row * size) + column : (column * size) + row] = value;
                }
            }

            if ((angle == 0) && luma && (size < 32))
            {
                for (int i = 0; i < size; i++)
                {
                    output[vertical ? i * size : i] = CoefficientMath.ClipSample(main[1] + ((side[i + 1] - side[0]) >> 1), bitDepth);
                }
            }
        }

        private static int InverseAngle(int angle)
        {
            switch (angle)
            {
                case 2:
                    return 4096;
                case 5:
                    return 1638;
                case 9:
                    return 910;
                case 13:
                    return 630;
                case 17:
                    return 482;
                case 21:
                    return 390;
                case 26:
                    return 315;
                case 32:
                    return 256;
                default:
                    throw new ArgumentOutOfRangeException(nameof(angle));
            }
        }
    }
}
