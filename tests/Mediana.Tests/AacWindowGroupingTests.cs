// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacWindowGroupingTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        public void EveryRateRetainsGroupedOrderScaleHistoryAndExactSections(int frequency) =>
            AacWindowGroupingTestInput.VerifyFrequency(frequency);

        [Theory]
        [InlineData(0, 0, 1, 1400)]
        [InlineData(3, 1, 2, 4000)]
        [InlineData(6, 2, 2, 3200)]
        [InlineData(11, 3, 1, 2500)]
        public void ShortGroupsShareTheElementBudgetWithoutChangingAnyQuantizedCoefficient(int frequency, int grouping,
            int channels, int budget) => AacWindowGroupingTestInput.VerifyAllocation(frequency, grouping, channels, budget);

        [Fact]
        public void IncompleteAndOutOfRangeGroupsFail()
        {
            int[][] invalid = [[], [7], [9], [0, 8], [1, 1, 1, 1, 1, 1, 1], [1, 1, 1, 1, 1, 1, 1, 1, 1]];
            foreach (int[] groups in invalid)
            {
                Assert.ThrowsAny<ArgumentException>(() =>
                {
                    _ = new AacEncodingWindow(3, 2, 0, groups, new int[129]);
                });
            }
            Assert.Throws<ArgumentException>(() =>
            {
                _ = new AacEncodingWindow(3, 0, 0, [8], new int[129]);
            });
        }
    }
}
