// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1GrainNoise
    {
        internal const int LumaWidth = 82, LumaHeight = 73;
        internal const int ChromaWidth = 44, ChromaHeight = 38;
        internal const int LumaLength = LumaWidth * LumaHeight;
        internal const int ChromaLength = ChromaWidth * ChromaHeight;

        internal static void Generate(Av1GrainParameters parameters, int bitDepth, bool monochrome,
            Span<short> luma, Span<short> u, Span<short> v, CancellationToken token)
        {
            int minimum = -(128 << (bitDepth - 8));
            int maximum = -minimum - 1;
            White(parameters.Seed, !parameters.LumaPoints.IsEmpty, bitDepth, parameters.GrainShift, luma);
            if (!parameters.LumaPoints.IsEmpty)
            {
                Filter(parameters, parameters.LumaCoefficients.Span, luma, LumaWidth, LumaHeight, default, minimum, maximum, token);
            }
            if (monochrome) { return; }
            bool hasU = parameters.ChromaScalingFromLuma || !parameters.U.Points.IsEmpty;
            bool hasV = parameters.ChromaScalingFromLuma || !parameters.V.Points.IsEmpty;
            White((ushort)(parameters.Seed ^ 0xb524), hasU, bitDepth, parameters.GrainShift, u);
            White((ushort)(parameters.Seed ^ 0x49d8), hasV, bitDepth, parameters.GrainShift, v);
            if (hasU) { Filter(parameters, parameters.U.Coefficients.Span, u, ChromaWidth, ChromaHeight, luma, minimum, maximum, token); }
            if (hasV) { Filter(parameters, parameters.V.Coefficients.Span, v, ChromaWidth, ChromaHeight, luma, minimum, maximum, token); }
        }

        private static void White(ushort seed, bool active, int bitDepth, int grainShift, Span<short> grain)
        {
            if (!active) { grain.Clear(); return; }
            Av1GrainRandom random = new(seed);
            int shift = 12 - bitDepth + grainShift;
            ReadOnlySpan<short> gaussian = Av1GrainGaussian.Samples;
            for (int index = 0; index < grain.Length; index++) { grain[index] = (short)Round(gaussian[random.Next(11)], shift); }
        }

        private static void Filter(Av1GrainParameters parameters, ReadOnlySpan<sbyte> coefficients, Span<short> grain,
            int width, int height, ReadOnlySpan<short> luma, int minimum, int maximum, CancellationToken token)
        {
            int lag = parameters.Lag;
            for (int y = 3; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 3; x < width - 3; x++)
                {
                    int sum = 0, position = 0;
                    for (int row = -lag; row <= 0; row++)
                    {
                        for (int column = -lag; column <= lag; column++)
                        {
                            if (row == 0 && column == 0)
                            {
                                if (!luma.IsEmpty && !parameters.LumaPoints.IsEmpty)
                                {
                                    int offset = ((y - 3) * 2 + 3) * LumaWidth + (x - 3) * 2 + 3;
                                    int value = Round(luma[offset] + luma[offset + 1] + luma[offset + LumaWidth] + luma[offset + LumaWidth + 1], 2);
                                    sum += value * coefficients[position];
                                }
                                break;
                            }
                            sum += grain[(y + row) * width + x + column] * coefficients[position++];
                        }
                    }
                    int result = grain[y * width + x] + Round(sum, parameters.CoefficientShift);
                    grain[y * width + x] = (short)Math.Clamp(result, minimum, maximum);
                }
            }
        }

        internal static int Round(int value, int shift) => shift == 0 ? value : (value + (1 << (shift - 1))) >> shift;
    }
}
