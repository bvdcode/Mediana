// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264BlockRowsTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(32)]
        [InlineData(21)]
        public void CopiesOnlyVisibleBytesBetweenUnalignedPaddedRows(int width)
        {
            const int rows = 5;
            int inputStride = width + 3;
            int outputStride = width + 7;
            byte[] input = new byte[1 + (rows - 1) * inputStride + width];
            new Random(924).NextBytes(input);
            byte[] output = new byte[2 + (rows - 1) * outputStride + width];
            output.AsSpan().Fill(0x5a);
            byte[] expected = (byte[])output.Clone();
            for (int row = 0; row < rows; row++)
            {
                Array.Copy(input, 1 + row * inputStride, expected, 2 + row * outputStride, width);
            }
            H264BlockRows.Copy(input.AsSpan(1), inputStride, output.AsSpan(2), outputStride, width, rows);
            Assert.Equal(expected, output);
        }
    }
}
