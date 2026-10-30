// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Mediana.Core.Timing;
using Microsoft.Extensions.Logging;

namespace Mediana.Tests
{
    public class MediaProgressTests
    {
        [Fact]
        public void ProgressReportsImmediatelyThenOncePerSecondAndOnCompletion()
        {
            ProgressTestContext context = new();
            MediaProgress progress = new(context, context);
            progress.Report(new MediaDuration(0, new TimeBase(1, 48000)), 44);
            Assert.Single(context.Messages);
            Assert.Contains("time=00:00:00", context.Messages[0], StringComparison.Ordinal);
            Assert.Contains("speed=0.00x", context.Messages[0], StringComparison.Ordinal);
            context.Advance(TimeSpan.FromMilliseconds(500));
            progress.Report(new MediaDuration(48000, new TimeBase(1, 48000)), 1024 * 1024);
            Assert.Single(context.Messages);
            context.Advance(TimeSpan.FromMilliseconds(500));
            progress.Report(new MediaDuration(96000, new TimeBase(1, 48000)), 2 * 1024 * 1024);
            Assert.Equal(2, context.Messages.Count);
            Assert.Contains("time=00:00:02", context.Messages[1], StringComparison.Ordinal);
            Assert.Contains("speed=2.00x", context.Messages[1], StringComparison.Ordinal);
            Assert.Contains("size=2.00 MiB", context.Messages[1], StringComparison.Ordinal);
            progress.Report(new MediaDuration(96000, new TimeBase(1, 48000)), 2 * 1024 * 1024, completed: true);
            Assert.Equal(3, context.Messages.Count);
        }

        [Fact]
        public void ProgressDoesNotWrapLongDurationsOrInventAPercentage()
        {
            ProgressTestContext context = new();
            MediaProgress progress = new(context, context);
            context.Advance(TimeSpan.FromSeconds(10));
            progress.Report(new MediaDuration(48000L * 25 * 60 * 60, new TimeBase(1, 48000)), 0);
            Assert.Contains("time=1.01:00:00", Assert.Single(context.Messages), StringComparison.Ordinal);
            Assert.DoesNotContain("%", context.Messages[0], StringComparison.Ordinal);
            Assert.DoesNotContain("eta=", context.Messages[0], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProgressFormatsSampleTimeWithoutFloatingPointArtifacts()
        {
            ProgressTestContext context = new();
            MediaProgress progress = new(context, context);
            progress.Report(new MediaDuration(10368, new TimeBase(1, 48000)), 0);
            Assert.Contains("time=00:00:00.216 ", Assert.Single(context.Messages), StringComparison.Ordinal);
        }

        [Fact]
        public void EstimateAppearsAfterWarmupAndDisappearsOnStallAndCompletion()
        {
            ProgressTestContext context = new();
            MediaProgress progress = new(context, context, new MediaDuration(600, new TimeBase(1, 1)));
            progress.Report(new MediaDuration(0, new TimeBase(1, 1)), 0);
            context.Advance(TimeSpan.FromSeconds(1));
            progress.Report(new MediaDuration(100, new TimeBase(1, 1)), 0);
            Assert.All(context.Messages, message => Assert.DoesNotContain("eta=", message, StringComparison.OrdinalIgnoreCase));
            context.Advance(TimeSpan.FromSeconds(1));
            progress.Report(new MediaDuration(200, new TimeBase(1, 1)), 0);
            Assert.Contains("eta=00:00:04", context.Messages[^1], StringComparison.Ordinal);
            context.Advance(TimeSpan.FromSeconds(1));
            progress.Report(new MediaDuration(200, new TimeBase(1, 1)), 0);
            Assert.DoesNotContain("eta=", context.Messages[^1], StringComparison.OrdinalIgnoreCase);
            progress.Report(new MediaDuration(600, new TimeBase(1, 1)), 0, completed: true);
            Assert.DoesNotContain("eta=", context.Messages[^1], StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.None)]
        public void QuietLogLevelsSuppressProgress(LogLevel level)
        {
            ProgressTestContext context = new() { MinimumLevel = level };
            MediaProgress progress = new(context, context);
            progress.Report(new MediaDuration(48000, new TimeBase(1, 48000)), 1024);
            progress.Report(new MediaDuration(48000, new TimeBase(1, 48000)), 1024, completed: true);
            Assert.Empty(context.Messages);
        }
    }
}
