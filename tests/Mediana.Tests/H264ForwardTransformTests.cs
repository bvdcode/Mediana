// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264ForwardTransformTests
    {
        [Fact]
        public void CompleteBlocksMatchMatrixMultiplicationAndPreserveOverlappingStorage() => H264ForwardTransformTestInput.Verify();

        [Fact]
        public void RepeatedTransformsAllocateNoHeapStorage() => H264ForwardTransformTestInput.VerifyAllocation();

        [Fact]
        public void PairedByteResidualsMatchIndependentMatrixAndQuantization() => H264BlockPairTestInput.Verify();

        [Fact]
        public void QuantizationMatchesScalarEquationsAtAllQuantizersAndRoundingBoundaries() => H264ForwardQuantizationTestInput.Verify();

        [Fact]
        public void ConstantResidualsMatchTheFullTransformAtEveryQuantizer()
        {
            Span<int> samples = stackalloc int[16];
            Span<int> transformed = stackalloc int[16];
            Span<int> luma = stackalloc int[16];
            Span<int> dc = stackalloc int[4];
            Span<int> chroma = stackalloc int[4];
            for (int qp = 0; qp <= 51; qp++)
            {
                for (int difference = -255; difference <= 255; difference++)
                {
                    samples.Fill(difference);
                    H264ForwardTransform.Transform4x4(samples, transformed);
                    H264ForwardTransform.Quantize4x4(transformed, qp, luma);
                    dc.Fill(transformed[0]);
                    H264ForwardTransform.ChromaDc(dc, qp, chroma);
                    Assert.Equal(luma[0], H264ForwardTransform.QuantizeConstantBlock(difference, qp, false));
                    Assert.Equal(chroma[0], H264ForwardTransform.QuantizeConstantBlock(difference, qp, true));
                    Assert.False(luma[1..].ContainsAnyExcept(0));
                    Assert.False(chroma[1..].ContainsAnyExcept(0));
                }
            }
        }

        [Fact]
        public void SkipBoundsQuantizeToZeroEvenAtTheLargestTransformResponse()
        {
            int[][] signs = [[1, 1, 1, 1], [1, 1, -1, -1], [1, -1, -1, 1], [1, -1, 1, -1]];
            Span<int> samples = stackalloc int[16];
            Span<int> transformed = stackalloc int[16];
            Span<int> levels = stackalloc int[16];
            for (int qp = 0; qp <= 51; qp++)
            {
                H264SkipResidualBounds bounds = H264ForwardTransform.GetSkipBounds(qp);
                foreach (int[] row in signs)
                {
                    foreach (int[] column in signs)
                    {
                        for (int i = 0; i < 16; i++) { samples[i] = row[i / 4] * column[i % 4] * bounds.Luma; }
                        H264ForwardTransform.Transform4x4(samples, transformed);
                        H264ForwardTransform.Quantize4x4(transformed, qp, levels);
                        Assert.False(levels.ContainsAnyExcept(0));
                    }
                }
                int chromaQp = H264InverseTransform.ChromaQp(qp, 0);
                Assert.Equal(0, H264ForwardTransform.QuantizeConstantBlock(bounds.Chroma, chromaQp, true));
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(52)]
        public void InvalidQuantizersLeaveOutputUntouched(int qp)
        {
            int[] output = Enumerable.Repeat(93, 16).ToArray();
            Assert.Throws<ArgumentException>(() => H264ForwardTransform.Quantize4x4(new int[16], qp, output));
            Assert.All(output, value => Assert.Equal(93, value));
        }

        [Fact]
        public void UnrepresentableCoefficientMagnitudeIsRejected()
        {
            int[] input = new int[16];
            input[0] = int.MinValue;
            Assert.Throws<OverflowException>(() => H264ForwardTransform.Quantize4x4(input, 26, new int[16]));
        }

        [Theory]
        [InlineData(0, 16)]
        [InlineData(15, 16)]
        [InlineData(17, 16)]
        [InlineData(16, 0)]
        [InlineData(16, 15)]
        [InlineData(16, 17)]
        public void InvalidDimensionsLeaveOutputUntouched(int inputLength, int outputLength)
        {
            int[] output = Enumerable.Repeat(93, outputLength).ToArray();
            Assert.Throws<ArgumentException>(() => H264ForwardTransform.Transform4x4(new int[inputLength], output));
            Assert.All(output, value => Assert.Equal(93, value));
        }
    }
}
