// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Aac
{
    internal static class AacFourier
    {
        public static void Transform(Span<Complex> values, ReadOnlySpan<Complex> rotations, CancellationToken cancellationToken)
        {
            for (int start = 0; start < values.Length; start += 4)
            {
                Complex first = values[start];
                Complex second = values[start + 1];
                Complex third = values[start + 2];
                Complex fourth = values[start + 3];
                Complex evenSum = first + second;
                Complex evenDifference = first - second;
                Complex oddSum = third + fourth;
                Complex oddDifference = new(third.Imaginary - fourth.Imaginary, fourth.Real - third.Real);
                values[start] = evenSum + oddSum;
                values[start + 1] = evenDifference + oddDifference;
                values[start + 2] = evenSum - oddSum;
                values[start + 3] = evenDifference - oddDifference;
            }
            int width = 16;
            for (; width <= values.Length; width *= 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int quarter = width / 4;
                ReadOnlySpan<Complex> stage = rotations.Slice(quarter - 1, quarter * 3);
                for (int start = 0; start < values.Length; start += width)
                {
                    for (int offset = 0; offset < quarter; offset++)
                    {
                        int first = start + offset;
                        int second = first + quarter;
                        int third = second + quarter;
                        int fourth = third + quarter;
                        Complex a = values[first];
                        Complex b = Rotate(values[second], stage[quarter + offset]);
                        Complex c = Rotate(values[third], stage[offset]);
                        Complex d = Rotate(values[fourth], stage[2 * quarter + offset]);
                        Complex evenSum = a + b;
                        Complex evenDifference = a - b;
                        Complex oddSum = c + d;
                        Complex oddDifference = new(c.Imaginary - d.Imaginary, d.Real - c.Real);
                        values[first] = evenSum + oddSum;
                        values[second] = evenDifference + oddDifference;
                        values[third] = evenSum - oddSum;
                        values[fourth] = evenDifference - oddDifference;
                    }
                }
            }
            if (width / 4 != values.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int half = values.Length / 2;
                ReadOnlySpan<Complex> stage = rotations.Slice(half - 1, half);
                for (int offset = 0; offset < half; offset++)
                {
                    Complex original = values[offset];
                    Complex rotated = Rotate(values[offset + half], stage[offset]);
                    values[offset] = original + rotated;
                    values[offset + half] = original - rotated;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Complex Rotate(Complex value, Complex factor) => new(
            factor.Real * value.Real - factor.Imaginary * value.Imaginary,
            factor.Real * value.Imaginary + factor.Imaginary * value.Real);
    }
}
