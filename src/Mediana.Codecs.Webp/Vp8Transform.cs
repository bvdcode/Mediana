// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static class Vp8Transform
    {
        private const float Cosine = 1.306562965f;
        private const float Sine = 0.541196100f;

        internal static void Quantize(ReadOnlySpan<byte> source, int offset, int stride, ReadOnlySpan<byte> prediction,
            int dc, int ac, Span<short> quantized, Span<short> dequantized)
        {
            Span<float> horizontal = stackalloc float[16];
            Span<float> transformed = stackalloc float[16];
            for (int row = 0; row < 4; row++)
            {
                int input = offset + row * stride;
                int output = row * 4;
                float a = source[input] - prediction[output];
                float b = source[input + 1] - prediction[output + 1];
                float c = source[input + 2] - prediction[output + 2];
                float d = source[input + 3] - prediction[output + 3];
                Forward(a, b, c, d, horizontal, output, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                Forward(horizontal[column], horizontal[column + 4], horizontal[column + 8], horizontal[column + 12], transformed, column, 4);
            }
            for (int index = 0; index < 16; index++)
            {
                int step = index == 0 ? dc : ac;
                int value = (int)MathF.Round(transformed[index] / (2 * step), MidpointRounding.AwayFromZero);
                quantized[index] = checked((short)value);
                dequantized[index] = checked((short)(value * step));
            }
        }

        internal static void Reconstruct(ReadOnlySpan<short> coefficients, ReadOnlySpan<byte> prediction, Span<byte> destination, int offset, int stride)
        {
            Span<int> vertical = stackalloc int[16];
            Span<int> residual = stackalloc int[16];
            for (int column = 0; column < 4; column++)
            {
                Inverse(coefficients[column], coefficients[column + 4], coefficients[column + 8], coefficients[column + 12], vertical, column, 4);
            }
            for (int row = 0; row < 4; row++)
            {
                int start = row * 4;
                Inverse(vertical[start], vertical[start + 1], vertical[start + 2], vertical[start + 3], residual, start, 1);
                for (int column = 0; column < 4; column++)
                {
                    int index = start + column;
                    destination[offset + row * stride + column] = (byte)Math.Clamp(prediction[index] + ((residual[index] + 4) >> 3), 0, 255);
                }
            }
        }

        private static void Forward(float a, float b, float c, float d, Span<float> destination, int offset, int stride)
        {
            float sumOuter = a + d;
            float sumInner = b + c;
            float differenceOuter = a - d;
            float differenceInner = b - c;
            destination[offset] = sumOuter + sumInner;
            destination[offset + stride] = differenceOuter * Cosine + differenceInner * Sine;
            destination[offset + 2 * stride] = sumOuter - sumInner;
            destination[offset + 3 * stride] = differenceOuter * Sine - differenceInner * Cosine;
        }

        private static void Inverse(int dc, int low, int middle, int high, Span<int> destination, int offset, int stride)
        {
            int evenSum = dc + middle;
            int evenDifference = dc - middle;
            int oddDifference = ((low * 35468) >> 16) - high - ((high * 20091) >> 16);
            int oddSum = low + ((low * 20091) >> 16) + ((high * 35468) >> 16);
            destination[offset] = evenSum + oddSum;
            destination[offset + stride] = evenDifference + oddDifference;
            destination[offset + 2 * stride] = evenDifference - oddDifference;
            destination[offset + 3 * stride] = evenSum - oddSum;
        }
    }
}
