// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264Intra16Tests
    {
        [Fact]
        public void ForwardDcMatchesDirectHadamardMatrixAcrossEveryQuantizer()
        {
            int[,] matrix = { { 1, 1, 1, 1 }, { 1, 1, -1, -1 }, { 1, -1, -1, 1 }, { 1, -1, 1, -1 } };
            int[] scan = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
            int[] factors = [13107, 11916, 10082, 9362, 8192, 7282];
            Random random = new(491203);
            int[] input = new int[16];
            int[] expected = new int[16];
            int[] actual = new int[16];
            for (int qp = 0; qp <= 51; qp++)
            {
                for (int example = 0; example < 40; example++)
                {
                    for (int index = 0; index < input.Length; index++)
                    {
                        input[index] = random.Next(-4080, 4081);
                    }
                    int shift = 16 + qp / 6;
                    for (int index = 0; index < 16; index++)
                    {
                        int row = scan[index] / 4;
                        int column = scan[index] % 4;
                        long sum = 0;
                        for (int y = 0; y < 4; y++)
                        {
                            for (int x = 0; x < 4; x++)
                            {
                                sum += matrix[row, y] * (long)input[y * 4 + x] * matrix[column, x];
                            }
                        }
                        long value = (sum + 1) >> 1;
                        long magnitude = (Math.Abs(value) * factors[qp % 6] + (1L << shift) / 3) >> shift;
                        expected[index] = checked((int)(value < 0 ? -magnitude : magnitude));
                    }
                    H264ForwardTransform.LumaDc(input, qp, actual);
                    Assert.Equal(expected, actual);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(12)]
        [InlineData(26)]
        [InlineData(38)]
        [InlineData(51)]
        public async Task Intra16SyntaxMatchesTheDecoderAndReconstruction(int qp)
        {
            (byte[] transport, byte[] pixels) = await H264Intra16TestInput.EncodeAsync(qp);
            Assert.NotEmpty(transport);
            Assert.Equal(3072, pixels.Length);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264", $"intra16-qp{qp}.yuv"));
            Assert.Equal(expected, pixels);
        }
    }
}
