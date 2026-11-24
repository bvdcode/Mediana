// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264InverseReconstructionTests
    {
        [Theory]
        [InlineData(false, 4, 7)]
        [InlineData(false, 16, 21)]
        [InlineData(true, 8, 13)]
        [InlineData(true, 16, 4)]
        public void StridedReconstructionMatchesWideTransform(bool separateDc, int predictionStride, int stride)
        {
            Random random = new(43199);
            int[] levels = new int[separateDc ? 15 : 16];
            int[] residual = new int[16];
            byte[] prediction = new byte[predictionStride * 3 + 4];
            byte[] actual = new byte[stride * 4 + 5];
            byte[] alignedPrediction = new byte[stride * 4];
            byte[] source = new byte[stride * 4];
            for (int qp = 0; qp <= 51; qp++)
            {
                for (int index = 0; index < 35; index++)
                {
                    random.NextBytes(prediction);
                    random.NextBytes(source);
                    Array.Clear(levels);
                    if (index < levels.Length)
                    {
                        levels[index] = random.Next(-128, 129);
                    }
                    else if (index > 16)
                    {
                        for (int i = 0; i < levels.Length; i++)
                        {
                            levels[i] = random.Next(-16384, 16385);
                        }
                    }
                    int? dc = null;
                    if (separateDc)
                    {
                        dc = index % 3 == 0 ? random.Next(-(1 << 27), (1 << 27) + 1) : random.Next(-512, 513);
                    }
                    H264InverseTransform.Residual4x4(levels, qp, residual, dc);
                    actual.AsSpan().Fill(117);
                    byte[] expected = actual.ToArray();
                    int expectedError = 0;
                    for (int y = 0; y < 4; y++)
                    {
                        prediction.AsSpan(y * predictionStride, 4).CopyTo(alignedPrediction.AsSpan(y * stride, 4));
                        for (int x = 0; x < 4; x++)
                        {
                            int offset = y * stride + x;
                            byte sample = (byte)Math.Clamp(alignedPrediction[offset] + residual[4 * y + x], 0, 255);
                            expected[3 + offset] = sample;
                            int error = source[offset] - sample;
                            expectedError += error * error;
                        }
                    }
                    Assert.True(H264InverseTransform.TryReconstructFrame4x4(levels, qp, dc, prediction, predictionStride, actual.AsSpan(3), stride));
                    Assert.Equal(expected, actual);

                    actual.AsSpan().Fill(117);
                    Assert.True(H264InverseTransform.TryReconstructAndMeasure(levels, qp, dc, alignedPrediction, source,
                        actual.AsSpan(3), stride, out int measuredError));
                    Assert.Equal(expectedError, measuredError);
                    Assert.Equal(expected, actual);
                }
            }
        }

        [Theory]
        [InlineData(16385, 0)]
        [InlineData(-16385, 0)]
        [InlineData(1, int.MinValue)]
        [InlineData(1, int.MaxValue)]
        public void WideCoefficientLeavesDestinationUntouched(int level, int dc)
        {
            int[] levels = new int[15];
            levels[6] = level;
            byte[] output = Enumerable.Repeat((byte)57, 16).ToArray();
            Assert.False(H264InverseTransform.TryReconstructFrame4x4(levels, 51, dc, new byte[16], 4, output, 4));
            Assert.All(output, sample => Assert.Equal((byte)57, sample));
        }
    }
}
