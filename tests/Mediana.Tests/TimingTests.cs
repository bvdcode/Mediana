// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class TimingTests
    {
        [Theory]
        [InlineData(-3, TimestampRounding.TowardZero, -1)]
        [InlineData(-3, TimestampRounding.Floor, -2)]
        [InlineData(-3, TimestampRounding.Ceiling, -1)]
        [InlineData(-3, TimestampRounding.NearestEven, -2)]
        [InlineData(-5, TimestampRounding.NearestEven, -2)]
        [InlineData(3, TimestampRounding.NearestEven, 2)]
        [InlineData(5, TimestampRounding.NearestEven, 2)]
        [InlineData(5, TimestampRounding.Floor, 2)]
        [InlineData(5, TimestampRounding.Ceiling, 3)]
        public void RescaleRoundsSignedValues(long value, TimestampRounding rounding, long expected)
        {
            MediaTimestamp time = new(value, new TimeBase(1, 2));
            Assert.Equal(expected, time.Rescale(new TimeBase(1, 1), rounding).Value);
        }

        [Fact]
        public void EquivalentTimesHaveEqualHashCodes()
        {
            MediaTimestamp first = new(48_000, new TimeBase(1, 48_000));
            MediaTimestamp second = new(90_000, new TimeBase(1, 90_000));
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal(new TimeBase(1001, 30000), new TimeBase(2002, 60000));
            Assert.Equal(new MediaTimestamp(0, new TimeBase(1, 1)).GetHashCode(),
                new MediaTimestamp(0, new TimeBase(1, 48_000)).GetHashCode());
        }

        [Fact]
        public void RescaleDoesNotOverflowIntermediateProducts()
        {
            TimeBase basis = new(int.MaxValue, int.MaxValue - 1);
            MediaTimestamp time = new(long.MaxValue, basis);
            Assert.Equal(long.MaxValue, time.Rescale(basis, TimestampRounding.NearestEven).Value);
            Assert.True(time.CompareTo(new MediaTimestamp(long.MinValue, basis)) > 0);
            Assert.Throws<OverflowException>(() => time.Rescale(new TimeBase(1, int.MaxValue), TimestampRounding.Floor));
        }

        [Fact]
        public void InvalidTimeBasesAndRoundingAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeBase(0, 1));
            Assert.Throws<ArgumentException>(() => new MediaTimestamp(1, default));
            Assert.Throws<ArgumentException>(() => default(MediaTimestamp).CompareTo(default));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MediaTimestamp(1, new TimeBase(1, 1)).Rescale(new TimeBase(1, 1), (TimestampRounding)99));
        }
    }
}
