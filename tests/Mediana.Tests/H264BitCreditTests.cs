// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class H264BitCreditTests
    {
        [Fact]
        public void FractionalRefillSurvivesSeveralCommittedPictures()
        {
            H264BitCredit credit = new(10, 16, new(1, 3));
            credit.Commit(0, 2);
            Assert.Equal((Int128)10, credit.Available(1));
            Assert.Equal((Int128)20, credit.Available(2));
            Assert.Equal((Int128)30, credit.Available(3));
            credit.Commit(3, 1);
            Assert.Equal((Int128)6, credit.Available(3));
            Assert.Equal((Int128)16, credit.Available(4));
            Assert.Equal((Int128)26, credit.Available(5));
            credit.Commit(5, 1);
            Assert.Equal((Int128)2, credit.Available(5));
            Assert.Equal((Int128)12, credit.Available(6));
        }

        [Fact]
        public void HugeGapsSaturateWithoutTimestampOrRefillOverflow()
        {
            TimeBase clock = new(int.MaxValue, int.MaxValue - 1);
            H264BitCredit credit = new(60000000, 75000000, clock);
            Int128 capacity = (Int128)75000000 * clock.Denominator;
            credit.Commit(long.MinValue, 9375000);
            Assert.Equal((Int128)0, credit.Available(long.MinValue));
            Assert.Equal(capacity, credit.Available(long.MaxValue));
            credit.Commit(long.MaxValue, 1);
            Assert.Equal(capacity - (Int128)8 * clock.Denominator, credit.Available(long.MaxValue));
        }

        [Fact]
        public void RejectedDebitDoesNotSpendCreditOrAdvanceTheClock()
        {
            H264BitCredit credit = new(8, 8, new(1, 1));
            Assert.Throws<InvalidOperationException>(() => credit.Commit(100, 2));
            Assert.Equal((Int128)8, credit.Available(-100));
            credit.Commit(-100, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => credit.Available(-101));
            Assert.Throws<ArgumentOutOfRangeException>(() => credit.Commit(-99, -1));
            Assert.Equal((Int128)8, credit.Available(-99));
        }

        [Fact]
        public void ResetRestoresCapacityAndPermitsANewNegativeOrigin()
        {
            H264BitCredit credit = new(1, 16, new(1, 1000));
            credit.Commit(100, 2);
            Assert.Equal((Int128)0, credit.Available(100));
            credit.Reset();
            Assert.Equal((Int128)16000, credit.Available(long.MinValue));
            credit.Commit(long.MinValue, 1);
            Assert.Equal((Int128)8000, credit.Available(long.MinValue));
        }
    }
}
