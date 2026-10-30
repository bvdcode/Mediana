// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacEncodingMathTests
    {
        [Theory]
        [InlineData(128)]
        [InlineData(1024)]
        public void FoldedForwardTransformMatchesTheDirectAnalysisEquation(int size)
        {
            double[] input = Enumerable.Range(0, size * 2).Select(index => Math.Sin(index * 0.91) * (index % 11 - 5)).ToArray();
            double[] spectrum = new double[size];
            AacMdct.Transform(input, spectrum, new double[size], new double[2 * size], new Complex[size / 2], CancellationToken.None);
            for (int bin = 0; bin < size; bin++)
            {
                double expected = 0;
                for (int sample = 0; sample < input.Length; sample++)
                {
                    expected += 2 * input[sample] * Math.Cos(Math.PI / size * (sample + 0.5 + size / 2) * (bin + 0.5));
                }
                Assert.InRange(Math.Abs(spectrum[bin] - expected), 0, 1e-8);
            }
        }

        [Theory]
        [InlineData(1, -1, 1)]
        [InlineData(3, -2, 2)]
        [InlineData(5, -4, 4)]
        [InlineData(7, -7, 7)]
        [InlineData(9, -12, 12)]
        [InlineData(11, -8191, 8191)]
        public void SpectralCodewordsPreserveValuesAndExactBitCounts(int codebook, int minimum, int maximum)
        {
            int dimensions = AacSpectralCodebook.Get(codebook).Dimensions;
            foreach (int value in new[] { minimum, -17, -16, -1, 0, 1, 16, 17, maximum }.Where(value => value >= minimum && value <= maximum))
            {
                int[] tuple = Enumerable.Range(0, dimensions).Select(index => index % 2 == 0 ? value : -value).ToArray();
                byte[] encoded = new byte[32];
                AacBitWriter writer = new(encoded);
                AacHuffmanEncoder.Write(ref writer, codebook, tuple);
                Assert.Equal(AacHuffmanEncoder.Count(codebook, tuple), writer.Position);
                AacBitReader reader = new(encoded);
                int[] actual = new int[dimensions];
                AacHuffman.ReadSpectrum(ref reader, codebook, actual);
                Assert.Equal(tuple, actual);
            }
        }
    }
}
