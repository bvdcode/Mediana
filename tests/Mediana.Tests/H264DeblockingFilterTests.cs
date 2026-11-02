// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264DeblockingFilterTests
    {
        [Fact]
        public void EveryMixedBoundaryStrengthAndOrientationMatchesIndependentEquations() => H264DeblockingFilterTestInput.Verify();

        [Fact]
        public void EdgeFilteringAllocatesNoHeapStorage() => H264DeblockingFilterTestInput.VerifyAllocation();
    }
}
