// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal class MediaTimeEstimator(MediaDuration? totalDuration)
    {
        private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan Smoothing = TimeSpan.FromSeconds(5);
        private TimeSpan firstElapsed;
        private TimeSpan lastElapsed;
        private double lastSeconds;
        private double speed;
        private bool observed;
        private bool measured;

        public TimeSpan? Observe(MediaDuration processed, TimeSpan elapsed)
        {
            if (totalDuration is not MediaDuration total)
            {
                return null;
            }
            double seconds = (double)processed.Value * processed.TimeBase.Numerator / processed.TimeBase.Denominator;
            if (!observed || elapsed <= lastElapsed || seconds < lastSeconds)
            {
                firstElapsed = lastElapsed = elapsed;
                lastSeconds = seconds;
                observed = true;
                measured = false;
                return null;
            }
            double interval = (elapsed - lastElapsed).TotalSeconds;
            double delta = seconds - lastSeconds;
            double currentSpeed = delta / interval;
            double weight = -double.ExpM1(-interval / Smoothing.TotalSeconds);
            if (delta > 0 || measured)
            {
                speed = measured ? speed + weight * (currentSpeed - speed) : currentSpeed;
                measured = true;
            }
            lastElapsed = elapsed;
            lastSeconds = seconds;
            double remaining = (double)total.Value * total.TimeBase.Numerator / total.TimeBase.Denominator - seconds;
            if (elapsed - firstElapsed < Warmup || delta <= 0 || speed <= 0 || remaining <= 0)
            {
                return null;
            }
            double estimate = Math.Ceiling(remaining / speed);
            if (!double.IsFinite(estimate) || estimate >= TimeSpan.MaxValue.TotalSeconds)
            {
                return null;
            }
            return TimeSpan.FromSeconds(estimate);
        }
    }
}
