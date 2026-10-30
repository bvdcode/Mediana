// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MediaTimeEstimatorTests
    {
        private static MediaDuration Seconds(long value) => new(value, new TimeBase(1, 1));

        [Fact]
        public void UnknownTotalNeverProducesAnEstimate()
        {
            MediaTimeEstimator estimator = new(null);
            Assert.Null(estimator.Observe(Seconds(0), TimeSpan.Zero));
            Assert.Null(estimator.Observe(Seconds(500), TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void StartupIsExcludedAndDifferentTimeBasesAreSupported()
        {
            MediaTimeEstimator estimator = new(new MediaDuration(60000, new TimeBase(1, 100)));
            Assert.Null(estimator.Observe(Seconds(100), TimeSpan.FromSeconds(10)));
            Assert.Null(estimator.Observe(new MediaDuration(200 * 48000, new TimeBase(1, 48000)), TimeSpan.FromSeconds(11)));
            Assert.Equal(TimeSpan.FromSeconds(3), estimator.Observe(Seconds(300), TimeSpan.FromSeconds(12)));
        }

        [Fact]
        public void EstimateUsesRecentSmoothedPaceInsteadOfLastBlockOrLifetimeAverage()
        {
            MediaTimeEstimator estimator = new(Seconds(10000));
            estimator.Observe(Seconds(0), TimeSpan.Zero);
            estimator.Observe(Seconds(100), TimeSpan.FromSeconds(1));
            Assert.Equal(TimeSpan.FromSeconds(98), estimator.Observe(Seconds(200), TimeSpan.FromSeconds(2)));
            TimeSpan estimate = Assert.IsType<TimeSpan>(estimator.Observe(Seconds(230), TimeSpan.FromSeconds(3)));
            Assert.InRange(estimate.TotalSeconds, 110, 114);
        }

        [Fact]
        public void IdleTimeDoesNotDiluteTheFirstMeasuredPace()
        {
            MediaTimeEstimator estimator = new(Seconds(1000));
            estimator.Observe(Seconds(0), TimeSpan.Zero);
            Assert.Null(estimator.Observe(Seconds(0), TimeSpan.FromSeconds(1)));
            Assert.Equal(TimeSpan.FromSeconds(18), estimator.Observe(Seconds(100), TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public void RewindingOrNonIncreasingWallTimeRestartsWarmup()
        {
            MediaTimeEstimator estimator = new(Seconds(1000));
            estimator.Observe(Seconds(0), TimeSpan.Zero);
            Assert.Equal(TimeSpan.FromSeconds(18), estimator.Observe(Seconds(100), TimeSpan.FromSeconds(2)));
            Assert.Null(estimator.Observe(Seconds(90), TimeSpan.FromSeconds(3)));
            Assert.Equal(TimeSpan.FromSeconds(17), estimator.Observe(Seconds(190), TimeSpan.FromSeconds(5)));
            Assert.Null(estimator.Observe(Seconds(200), TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void StallReachedTotalAndOutOfRangeEstimateAreUnknown()
        {
            MediaTimeEstimator estimator = new(Seconds(600));
            estimator.Observe(Seconds(0), TimeSpan.Zero);
            Assert.Null(estimator.Observe(Seconds(0), TimeSpan.FromSeconds(2)));
            Assert.Null(estimator.Observe(Seconds(600), TimeSpan.FromSeconds(3)));
            Assert.Null(estimator.Observe(Seconds(601), TimeSpan.FromSeconds(4)));
            MediaTimeEstimator unrepresentable = new(new MediaDuration(long.MaxValue, new TimeBase(int.MaxValue, 1)));
            unrepresentable.Observe(Seconds(0), TimeSpan.Zero);
            Assert.Null(unrepresentable.Observe(Seconds(1), TimeSpan.FromSeconds(2)));
        }
    }
}
