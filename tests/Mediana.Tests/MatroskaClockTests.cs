// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MatroskaClockTests
    {
        [Theory]
        [InlineData(1, 333000000, true)]
        [InlineData(1, 334000000, true)]
        [InlineData(1, 335000000, false)]
        [InlineData(0.5, 333500000, true)]
        [InlineData(0.5, 333000000, true)]
        [InlineData(0.5, 334000000, false)]
        public void AudioContinuityUsesTheDeclaredTrackTickPrecision(double trackScale, long nanoseconds, bool matches)
        {
            MatroskaTrackClock clock = new(1000000, new(trackScale, null, 0, 0));
            Assert.Equal(matches, clock.MatchesTimestampPrecision(new(nanoseconds, MatroskaTrackClock.TimeBase), new(1, new(1, 3))));
            Assert.Equal(matches, clock.MatchesTimestampPrecision(new(-nanoseconds, MatroskaTrackClock.TimeBase), new(-1, new(1, 3))));
        }

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
