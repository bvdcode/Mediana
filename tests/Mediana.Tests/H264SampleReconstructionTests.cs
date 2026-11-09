// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264SampleReconstructionTests
    {
        [Theory]
        [InlineData(4, 4)]
        [InlineData(4, 19)]
        [InlineData(8, 8)]
        [InlineData(8, 27)]
        public void ReconstructedSamplesClipFullIntegerResidualsAndPreservePadding(int size, int stride)
        {
            Random random = new(7301);
            byte[] prediction = new byte[size * size];
            int[] residual = new int[size * size];
            byte[] actual = new byte[stride * size + 9];
            for (int iteration = 0; iteration < 256; iteration++)
            {
                random.NextBytes(prediction);
                FillResiduals(random, residual, iteration);
                actual.AsSpan().Fill(193);
                byte[] expected = actual.ToArray();
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int index = y * size + x;
                        expected[y * stride + x + 3] = (byte)Math.Clamp((long)prediction[index] + residual[index], 0, 255);
                    }
                }
                H264SampleReconstruction.Write(prediction, residual, size, actual.AsSpan(3), stride);
                Assert.Equal(expected, actual);
            }
        }

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(23)]
        public void DistortionUsesClippedPixelsWithExactSquaredError(int stride)
        {
            Random random = new(8301);
            byte[] source = new byte[stride * 4 + 9];
            byte[] prediction = new byte[source.Length];
            byte[] actual = new byte[source.Length];
            int[] residual = new int[16];
            for (int iteration = 0; iteration < 256; iteration++)
            {
                random.NextBytes(source);
                random.NextBytes(prediction);
                FillResiduals(random, residual, iteration);
                actual.AsSpan().Fill(193);
                byte[] expected = actual.ToArray();
                long squared = 0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int index = y * stride + x + 3;
                        int sample = (int)Math.Clamp((long)prediction[index] + residual[y * 4 + x], 0, 255);
                        expected[index] = (byte)sample;
                        long difference = source[index] - sample;
                        squared += difference * difference;
                    }
                }
                long measured = H264SampleReconstruction.WriteAndMeasure(source.AsSpan(3), prediction.AsSpan(3), stride,
                    residual, actual.AsSpan(3));
                Assert.Equal(squared, measured);
                Assert.Equal(expected, actual);
            }
        }

        private static void FillResiduals(Random random, Span<int> residual, int iteration)
        {
            ReadOnlySpan<int> boundaries = [int.MinValue, -32769, -32768, -256, -255, -1, 0, 1, 255, 256, 32767, 32768, int.MaxValue];
            for (int index = 0; index < residual.Length; index++)
            {
                residual[index] = iteration < boundaries.Length ? boundaries[(iteration + index) % boundaries.Length]
                    : (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
            }
        }
    }
}
