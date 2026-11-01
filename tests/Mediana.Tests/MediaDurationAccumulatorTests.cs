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
        public void ChangedTimeBaseDoesNotGuessAConversionOrRestoreTheTotal()
        {
            MediaDurationAccumulator total = new();
            total.Add(new MediaDuration(1, new(1, 24)));
            total.Add(new MediaDuration(1, new(1, 25)));
            Assert.Null(total.Duration);
            total.Add(new MediaDuration(1, new(1, 24)));
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
