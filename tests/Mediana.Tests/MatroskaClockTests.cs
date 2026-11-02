// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class MatroskaClockTests
    {
        [Fact]
        public Task SegmentDurationUsesExactFloatScaleAndNearestEvenNanoseconds() => MatroskaClockTestInput.VerifySegmentDurationsAsync();

        [Fact]
        public Task CompleteClockExpressionsMatchIndependentExactRationalVectors() => MatroskaClockTestInput.VerifyReferencesAsync();

        [Fact]
        public Task InvalidScalesAndUninitializedOffsetTimeBasesAreRejected() => MatroskaClockTestInput.VerifyInvalidAsync();

        [Fact]
        public void UnitScalePacketClockHasNoWarmedHeapAllocation() => MatroskaClockTestInput.VerifyAllocation();
    }
}
