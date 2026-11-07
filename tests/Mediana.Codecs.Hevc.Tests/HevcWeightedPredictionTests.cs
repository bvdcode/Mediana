// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcWeightedPredictionTests
    {
        [Fact]
        public void WeightedMainAndMain10PredictionsMatchTheIndependentReference()
        {
            Assert.Equal(2592, HevcWeightedPredictionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
        }

        [Fact]
        public void PredictionCombinationDoesNotAllocateWorkingStorage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            HevcWeightedPredictionTestInput.Verify(root);
            Assert.Equal(2592, HevcWeightedPredictionTestInput.Verify(root, true));
        }

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        public void DefaultWeightsPreserveIntegerSamplesAndAllowInPlaceOutput(int depth)
        {
            int shift = 14 - depth;
            int[] first = Enumerable.Range(0, 255).Select(i => i << shift).ToArray();
            HevcWeightedPrediction.Unidirectional(first, first, depth);
            Assert.Equal(Enumerable.Range(0, 255), first);
            int[] second = Enumerable.Range(0, 255).Select(i => (254 - i) << shift).ToArray();
            for (int i = 0; i < first.Length; i++) { first[i] <<= shift; }
            HevcWeightedPrediction.Bidirectional(first, second, first, depth);
            Assert.All(first, sample => Assert.Equal(127, sample));
        }

        [Theory]
        [InlineData(32767, -32768, 1, 2, 16383, -16384)]
        [InlineData(32767, -32768, -1, 2, -16383, 16384)]
        [InlineData(1, -1, 1, 2, 0, 0)]
        [InlineData(2, -2, 1, 2, 1, -1)]
        [InlineData(10000, -10000, 127, 1, 32767, -32768)]
        [InlineData(-123, 456, -128, -128, -123, 456)]
        public void MotionScalingRoundsSymmetricallyAndClips(int x, int y, int current, int reference, int expectedX, int expectedY)
        {
            HevcMotionVector actual = new HevcMotionVector((short)x, (short)y).Scale(current, reference);
            Assert.Equal(new HevcMotionVector((short)expectedX, (short)expectedY), actual);
        }

        [Fact]
        public void MotionDifferencesWrapAtSixteenBits()
        {
            Assert.Equal(new HevcMotionVector(-32768, 32767), new HevcMotionVector(32767, -32768).Add(1, -1));
            Assert.Throws<InvalidDataException>(() => new HevcMotionVector(1, 1).Scale(1, 0));
        }
    }
}
