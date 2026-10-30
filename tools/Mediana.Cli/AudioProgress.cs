// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class AudioProgress(ILogger logger, TimeProvider clock)
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
        private readonly long started = clock.GetTimestamp();
        private TimeSpan lastReport;
        private bool reported;

        public void Report(long samplesPerChannel, int sampleRate, long outputBytes, bool completed = false)
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
            double seconds = samplesPerChannel / (double)sampleRate;
            double speed = 0;
            if (elapsed > TimeSpan.Zero)
            {
                speed = seconds / elapsed.TotalSeconds;
            }
            TimeSpan mediaTime = TimeSpan.FromTicks(checked((long)((Int128)samplesPerChannel * TimeSpan.TicksPerSecond / sampleRate)));
            string timeFormat = @"hh\:mm\:ss\.fff";
            if (mediaTime.Days > 0)
            {
                timeFormat = @"d\.hh\:mm\:ss\.fff";
            }
            CliLog.Progress(logger, mediaTime.ToString(timeFormat, CultureInfo.InvariantCulture), speed, outputBytes / (1024d * 1024));
            lastReport = elapsed;
            reported = true;
        }
    }
}
