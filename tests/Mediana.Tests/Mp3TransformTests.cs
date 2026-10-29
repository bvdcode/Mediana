// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    public class Mp3TransformTests
    {
        [Fact]
        public void FactoredDctMatchesEveryBasisVectorAndDenseSignals()
        {
            for (int vector = 0; vector < 36; vector++)
            {
                double[] input = new double[32];
                for (int index = 0; index < input.Length; index++)
                {
                    input[index] = vector < 32 ? (index == vector ? 1 : 0)
                        : Math.Sin((vector + 1) * (index + 0.5)) * Math.Cos(index / 7.0);
                }
                double[] actual = input.ToArray();
                Mp3Dct32.Transform(actual);
                for (int frequency = 0; frequency < 32; frequency++)
                {
                    double expected = 0;
                    for (int sample = 0; sample < 32; sample++)
                    {
                        expected += input[sample] * Math.Cos(Math.PI / 32 * (sample + 0.5) * frequency);
                    }
                    Assert.InRange(Math.Abs(expected - actual[frequency]), 0, 1e-13);
                }
            }
        }
    }
}
