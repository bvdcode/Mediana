// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class MediaDurationAccumulatorTests
    {
        [Fact]
        public void EmptyInputIsUnknownAndZeroDurationIsKnown()
        {
            MediaDurationAccumulator total = new();
            Assert.Null(total.Duration);
            total.Add(new MediaDuration(0, new(1, 48000)));
            Assert.Equal(new MediaDuration(0, new(1, 48000)), total.Duration);
        }

        [Fact]
        public void VariableDurationsKeepExactTicksAndEquivalentTimeBases()
        {
            MediaDurationAccumulator total = new();
            total.Add(new MediaDuration(1001, new(1, 30000)));
            total.Add(new MediaDuration(2002, new(2, 60000)));
            total.Add(new MediaDuration(0, new(1, 30000)));
            Assert.Equal(new MediaDuration(3003, new(1, 30000)), total.Duration);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void MissingDurationMakesAllSubsequentTotalsUnknown(int missing)
        {
            MediaDurationAccumulator total = new();
            for (int index = 0; index < 4; index++)
            {
                total.Add(index == missing ? null : new MediaDuration(1, new(1, 24)));
                Assert.Equal(index < missing ? new MediaDuration(index + 1, new(1, 24)) : null, total.Duration);
            }
        }

        [Fact]
        public void ChangedTimeBasesAreSummedExactlyOnACommonClock()
        {
            MediaDurationAccumulator total = new();
            total.Add(new MediaDuration(1, new(1, 24)));
            total.Add(new MediaDuration(1, new(1, 25)));
            Assert.Equal(new MediaDuration(49, new(1, 600)), total.Duration);
            total.Add(new MediaDuration(1, new(1, 24)));
            Assert.Equal(new MediaDuration(74, new(1, 600)), total.Duration);
        }

        [Fact]
        public void FractionalTailAndReducibleClocksKeepEveryKnownTick()
        {
            MediaDurationAccumulator total = new();
            total.Add(new MediaDuration(83, new(1, 1000)));
            total.Add(new MediaDuration(41_666_667, new(1, 1_000_000_000)));
            Assert.Equal(new MediaDuration(124_666_667, new(1, 1_000_000_000)), total.Duration);
            total.Add(new MediaDuration(2000, new(1, 2_000_000_000)));
            Assert.Equal(new MediaDuration(124_667_667, new(1, 1_000_000_000)), total.Duration);
            total.Add(null);
            total.Add(new MediaDuration(1, new(1, 3)));
            Assert.Null(total.Duration);
        }

        [Fact]
        public void OverflowFailsWithoutWrappingOrChangingThePreviousTotal()
        {
            MediaDurationAccumulator total = new();
            total.Add(new MediaDuration(long.MaxValue, new(1, 48000)));
            Assert.Throws<OverflowException>(() => total.Add(new MediaDuration(1, new(1, 48000))));
            Assert.Equal(new MediaDuration(long.MaxValue, new(1, 48000)), total.Duration);
        }

        [Fact]
        public void InvalidDefaultDurationIsRejected()
        {
            MediaDurationAccumulator total = new();
            Assert.Throws<ArgumentException>(() => total.Add(default(MediaDuration)));
            Assert.Null(total.Duration);
        }
    }
}
