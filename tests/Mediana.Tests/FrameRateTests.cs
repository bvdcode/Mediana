// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class FrameRateTests
    {
        [Fact]
        public void FractionalRatesRetainExactNormalizedIdentity()
        {
            FrameRate rate = new(60000, 2002);
            Assert.Equal(new FrameRate(30000, 1001), rate);
            Assert.Equal(30000, rate.Numerator);
            Assert.Equal(1001, rate.Denominator);
            Assert.InRange(rate.ToDouble(), 29.97002997002996, 29.97002997002998);
            Assert.Equal(rate.GetHashCode(), new FrameRate(30000, 1001).GetHashCode());
        }

        [Fact]
        public void IndexedCountUsesTheWholeIntervalWithoutThirtyTwoBitRounding()
        {
            Assert.Equal(new FrameRate(30720, 7001), FrameRate.FromFrameCount(6, new(14002, new(1, 10240))));
            Assert.Equal(new FrameRate(30000, 1001), FrameRate.FromFrameCount(30, new(90090, new(1, 90000))));
            Assert.Equal(new FrameRate(3, 14), FrameRate.FromFrameCount(6, new(4, new(7, 1))));
        }

        [Fact]
        public void WideCountsAndClocksRemainExactWithoutOverflow()
        {
            FrameRate rate = FrameRate.FromFrameCount(long.MaxValue, new(1, new(1, int.MaxValue)))!.Value;
            Assert.Equal((Int128)long.MaxValue * int.MaxValue, rate.Numerator);
            Assert.Equal(1, rate.Denominator);
            Assert.Equal(new FrameRate(Int128.MaxValue, Int128.MaxValue), new FrameRate(1, 1));
            FrameRate slow = FrameRate.FromFrameCount(1, new(long.MaxValue, new(int.MaxValue, 1)))!.Value;
            Assert.Equal(1, slow.Numerator);
            Assert.Equal((Int128)long.MaxValue * int.MaxValue, slow.Denominator);
            Assert.True(double.IsFinite(rate.ToDouble()));
            Assert.True(slow.ToDouble() > 0);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void EmptyAndZeroIntervalsDoNotInventRates(long count, long ticks) =>
            Assert.Null(FrameRate.FromFrameCount(count, new(ticks, new(1, 1000))));

        [Fact]
        public void InvalidRatesAndClocksAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRate(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameRate.FromFrameCount(-1, new(1, new(1, 1))));
            Assert.Throws<ArgumentException>(() => FrameRate.FromFrameCount(0, default));
            Assert.Throws<ArgumentException>(() => default(FrameRate).Validate());
            Assert.Throws<ArgumentException>(() => default(FrameRate).ToDouble());
        }
    }
}
