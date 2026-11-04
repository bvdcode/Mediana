// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class TgsStrokeTests
    {
        [Fact]
        public void AllZeroDashPatternsProduceSolidOutlines() => TgsStrokeTestInput.VerifyZeroPatterns();

        [Fact]
        public void AnimatedDashHoldsItsExactFirstAndLastValues() => TgsStrokeTestInput.VerifyAnimatedZeroDash();
    }
}
