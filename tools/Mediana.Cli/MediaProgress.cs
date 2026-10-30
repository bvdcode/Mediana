// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Microsoft.Extensions.Logging;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal class MediaProgress(ILogger logger, TimeProvider clock, MediaDuration? totalDuration = null)
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
        private readonly MediaTimeEstimator estimator = new(totalDuration);
        private readonly long started = clock.GetTimestamp();
        private TimeSpan lastReport;
        private bool reported;

        public void Report(MediaDuration processed, long outputBytes, bool completed = false)
        {
            if (!logger.IsEnabled(LogLevel.Information))
            {
                return;
            }
            TimeSpan elapsed = clock.GetElapsedTime(started);
            if (reported && !completed && elapsed - lastReport < Interval)
            {
                return;
            }
            double seconds = (double)processed.Value * processed.TimeBase.Numerator / processed.TimeBase.Denominator;
            double speed = 0;
            if (elapsed > TimeSpan.Zero)
            {
                speed = seconds / elapsed.TotalSeconds;
            }
            TimeSpan mediaTime = TimeSpan.FromTicks(checked((long)((Int128)processed.Value * processed.TimeBase.Numerator
                * TimeSpan.TicksPerSecond / processed.TimeBase.Denominator)));
            TimeSpan? remaining = estimator.Observe(processed, elapsed);
            if (!completed && remaining is TimeSpan estimate)
            {
                CliLog.ProgressWithEstimate(logger, Format(mediaTime), speed, outputBytes / (1024d * 1024), Format(estimate, fractional: false));
            }
            else
            {
                CliLog.Progress(logger, Format(mediaTime), speed, outputBytes / (1024d * 1024));
            }
            lastReport = elapsed;
            reported = true;
        }

        private static string Format(TimeSpan time, bool fractional = true)
        {
            string timeFormat = @"hh\:mm\:ss";
            if (time.Days > 0)
            {
                timeFormat = @"d\.hh\:mm\:ss";
            }
            if (fractional)
            {
                timeFormat += @"\.fff";
            }
            return time.ToString(timeFormat, CultureInfo.InvariantCulture);
        }
    }
}
