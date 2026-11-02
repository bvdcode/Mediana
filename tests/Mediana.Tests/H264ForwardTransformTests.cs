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
