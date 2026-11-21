// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts.Tests
{
    public class DtsSynthesisTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FactoredSynthesisMatchesDirectCosineDefinitionAndFilterHistory(bool perfect)
        {
            double[] history = new double[576], direct = new double[576], bands = new double[32];
            float[] actual = new float[192];
            ReadOnlySpan<double> filter = perfect ? DtsFilterTables.Perfect : DtsFilterTables.NonPerfect;
            for (int block = 0; block < 40; block++)
            {
                for (int i = 0; i < 32; i++)
                {
                    bands[i] = block < 32 ? (i == block ? 100 : 0) : Math.Sin(i * 7.2 + block) * 30;
                }
                Array.Fill(actual, float.NaN);
                DtsSynthesis.Apply(bands, history, perfect, actual, 4, 6, 1);
                for (int k = 0; k < 32; k++)
                {
                    double sum = 0;
                    for (int n = 0; n < 32; n++)
                    {
                        sum += bands[n] * Math.Cos(Math.PI * (n + 0.5) * (k + 0.5) / 32);
                    }
                    direct[k] = sum * 0.5;
                }
                for (int i = 0; i < 32; i++)
                {
                    double first = direct[512 + i], next = 0;
                    for (int j = 0; j < 512; j += 64)
                    {
                        first += filter[i + j] * (direct[i + j] - direct[j + 31 - i]);
                        next += filter[i + j + 32] * (-direct[i + j] - direct[j + 31 - i]);
                    }
                    direct[512 + i] = next;
                    Assert.InRange(Math.Abs(actual[i * 6 + 4] - first / (65536 * Math.Sqrt(2))), 0, 1e-9);
                    Assert.True(float.IsNaN(actual[i * 6 + 3]));
                    Assert.True(float.IsNaN(actual[i * 6 + 5]));
                }
                direct.AsSpan(0, 480).CopyTo(direct.AsSpan(32));
            }
        }
    }
}
