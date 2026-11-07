// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class TransformTests
    {
        [Fact]
        public void IntegerDctMatchesDirectFloatingPointDefinition()
        {
            Random random = new(7529); int[] block = new int[64]; byte[] actual = new byte[64];
            double[] basis = new double[64];
            for (int x = 0; x < 8; x++)
            { for (int k = 0; k < 8; k++) { basis[x * 8 + k] = Math.Cos((2 * x + 1) * k * Math.PI / 16) * (k == 0 ? Math.Sqrt(0.5) : 1) / 2; } }
            for (int test = 0; test < 640; test++)
            {
                block.AsSpan().Clear();
                if (test < 128) { block[test % 64] = test < 64 ? 2047 : -2048; }
                else { for (int i = 0; i < 64; i++) { block[i] = random.Next(-256, 256); } }
                actual.AsSpan().Fill(128); Mpeg4InverseDct.Write(block, actual, 8, false);
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        double value = 0;
                        for (int v = 0; v < 8; v++)
                        { for (int u = 0; u < 8; u++) { value += block[v * 8 + u] * basis[x * 8 + u] * basis[y * 8 + v]; } }
                        int expected = Math.Clamp((int)Math.Floor(value + 128.5), 0, 255);
                        Assert.InRange(Math.Abs(actual[y * 8 + x] - expected), 0, 1);
                    }
                }
            }
        }
    }
}
