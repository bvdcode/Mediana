// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacEncodingQualityTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void QuietChannelRetainsItsNoiseFloorWithoutExceedingThePairBudget(int activeChannel) =>
            AacEncodingQualityTestInput.Verify(activeChannel);
    }
}
