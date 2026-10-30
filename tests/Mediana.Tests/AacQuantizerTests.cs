// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacQuantizerTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(255)]
        public void EveryReconstructionDecisionBoundarySelectsTheClosestSignedMagnitude(int scale)
        {
            double factor = Math.Pow(2, 0.25 * (scale - 100));
            double[] spectrum = new double[4];
            double[] powered = new double[4];
            int[] quantized = new int[4];
            for (int value = 0; value < 8191; value++)
            {
                double lower = Math.Pow(value, 4.0 / 3) * factor;
                double upper = Math.Pow(value + 1, 4.0 / 3) * factor;
                double midpoint = (lower + upper) * 0.5;
                double margin = (upper - lower) * 0.0001;
                spectrum[0] = midpoint - margin;
                spectrum[1] = midpoint + margin;
                spectrum[2] = -spectrum[0];
                spectrum[3] = -spectrum[1];
                for (int index = 0; index < spectrum.Length; index++)
                {
                    powered[index] = Math.Pow(Math.Abs(spectrum[index]), 0.75);
                }
                double error = AacQuantizer.Quantize(spectrum, powered, quantized, scale);
                Assert.Equal(new[] { value, value + 1, -value, -value - 1 }, quantized);
                double expected = 2 * Math.Pow(midpoint - margin - lower, 2) + 2 * Math.Pow(midpoint + margin - upper, 2);
                Assert.InRange(Math.Abs(error - expected), 0, expected * 1e-10 + double.Epsilon);
            }
        }

        [Fact]
        public void UnrepresentableMagnitudeCannotWrapIntoTheSpectralTuple()
        {
            double[] spectrum = [Math.Pow(8193, 4.0 / 3)];
            double[] powered = [8193];
            int[] quantized = new int[1];
            Assert.Equal(double.PositiveInfinity, AacQuantizer.Quantize(spectrum, powered, quantized, 100));
        }

        [Fact]
        public void AllDifferentialScaleSymbolsHaveTheExactNormativeLengthAndValue()
        {
            for (int difference = -60; difference <= 60; difference++)
            {
                byte[] encoded = new byte[8];
                AacBitWriter writer = new(encoded);
                AacHuffmanEncoder.WriteSymbol(ref writer, 0, difference + 60);
                Assert.Equal(AacHuffmanEncoder.CountSymbol(0, difference + 60), writer.Position);
                AacBitReader reader = new(encoded);
                Assert.Equal(difference, AacHuffman.ReadScaleFactor(ref reader));
            }
        }
    }
}
