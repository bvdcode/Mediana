// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264LevelBudgetTests
    {
        [Fact]
        public void InitialAndIdrPicturesKeepTheirOwnCompressionBound()
        {
            H264LevelBudget budget = new(new(1, 24), 3600, 64);
            Assert.Equal(691138, budget.MaximumPacketBytes(0, true));
            Assert.Throws<InvalidOperationException>(() => budget.Commit(0, true, 691139));
            budget.Commit(0, true, 1024);
            Assert.Equal(1966082, budget.MaximumPacketBytes(1, false));
            Assert.Equal(691138, budget.MaximumPacketBytes(1, true));
            budget.Commit(1, false, 1966082);
            budget.Reset();
            Assert.Equal(691138, budget.MaximumPacketBytes(-100, true));
        }

        [Fact]
        public void SmallPicturesRetainTheFractionalMaximumProcessingRateTerm()
        {
            H264LevelBudget budget = new(new(1, 24), 1, 64);
            Assert.Equal(274274, budget.MaximumPacketBytes(0, true));
            Assert.Throws<InvalidOperationException>(() => budget.Commit(0, true, 1000000));
            budget.Commit(0, true, 1024);
        }

        [Fact]
        public void VclCapacityAndFractionalRefillConstrainSustainedLargePictures()
        {
            H264LevelBudget budget = new(new(1, 24), 3600, 64);
            budget.Commit(0, true, 500000);
            for (int index = 1; index <= 16; index++)
            {
                budget.Commit(index, false, 700000);
            }
            Assert.Equal(539655, budget.MaximumPacketBytes(17, false));
            Assert.Throws<InvalidOperationException>(() => budget.Commit(17, false, 700000));
            Assert.Equal(539655, budget.MaximumPacketBytes(17, false));
            budget.Commit(17, false, 539655);
            Assert.Equal(260421, budget.MaximumPacketBytes(18, false));
            Assert.Equal(7812504, budget.MaximumPacketBytes(1000, false));
            Assert.Equal(691138, budget.MaximumPacketBytes(1000, true));
        }

        [Fact]
        public void RepeatedParameterSetsCanExhaustNalCapacityWhileVclStillFits()
        {
            H264LevelBudget budget = new(new(1, 30), 8192, 128000);
            for (int index = 0; index < 116; index++)
            {
                budget.Commit(index, true, 200004);
            }
            Assert.Equal(196898, budget.MaximumPacketBytes(116, true));
            Assert.Throws<InvalidOperationException>(() => budget.Commit(116, true, 200004));
            Assert.Equal(196898, budget.MaximumPacketBytes(116, true));
            budget.Commit(116, true, 196898);
            budget.Reset();
            Assert.Equal(1444866, budget.MaximumPacketBytes(-1, true));
        }

        [Theory]
        [InlineData(long.MinValue)]
        [InlineData(0L)]
        [InlineData(long.MaxValue - 200)]
        public void AbsoluteTimestampOriginsDoNotChangeAdmission(long origin)
        {
            H264LevelBudget budget = new(new(int.MaxValue, int.MaxValue - 1), 1, 64);
            Assert.Equal(274274, budget.MaximumPacketBytes(origin, true));
            budget.Commit(origin, true, 1024);
            Assert.Equal(7812504, budget.MaximumPacketBytes(origin + 1, false));
        }

        [Fact]
        public void HugeRemovalIntervalsSaturateBothBudgetsWithoutOverflow()
        {
            H264LevelBudget budget = new(new(int.MaxValue, int.MaxValue - 1), 3600, 64);
            budget.Commit(long.MinValue, true, 1024);
            Assert.Equal(7812504, budget.MaximumPacketBytes(long.MaxValue, false));
            budget.Commit(long.MaxValue, false, 7812504);
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.MaximumPacketBytes(0, true));
        }
    }
}
