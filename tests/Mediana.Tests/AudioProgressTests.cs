// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Cli;
using Microsoft.Extensions.Logging;

namespace Mediana.Tests
{
    public class AudioProgressTests
    {
        [Fact]
        public void ProgressReportsImmediatelyThenOncePerSecondAndOnCompletion()
        {
            ProgressTestContext context = new();
            AudioProgress progress = new(context, context);
            progress.Report(0, 48000, 44);
            Assert.Single(context.Messages);
            Assert.Contains("time=00:00:00", context.Messages[0], StringComparison.Ordinal);
            Assert.Contains("speed=0.00x", context.Messages[0], StringComparison.Ordinal);
            context.Advance(TimeSpan.FromMilliseconds(500));
            progress.Report(48000, 48000, 1024 * 1024);
            Assert.Single(context.Messages);
            context.Advance(TimeSpan.FromMilliseconds(500));
            progress.Report(96000, 48000, 2 * 1024 * 1024);
            Assert.Equal(2, context.Messages.Count);
            Assert.Contains("time=00:00:02", context.Messages[1], StringComparison.Ordinal);
            Assert.Contains("speed=2.00x", context.Messages[1], StringComparison.Ordinal);
            Assert.Contains("size=2.00 MiB", context.Messages[1], StringComparison.Ordinal);
            progress.Report(96000, 48000, 2 * 1024 * 1024, completed: true);
            Assert.Equal(3, context.Messages.Count);
        }

        [Fact]
        public void ProgressDoesNotWrapLongDurationsOrInventAPercentage()
        {
            ProgressTestContext context = new();
            AudioProgress progress = new(context, context);
            context.Advance(TimeSpan.FromSeconds(10));
            progress.Report(48000L * 25 * 60 * 60, 48000, 0);
            Assert.Contains("time=1.01:00:00", Assert.Single(context.Messages), StringComparison.Ordinal);
            Assert.DoesNotContain("%", context.Messages[0], StringComparison.Ordinal);
        }

        [Fact]
        public void ProgressFormatsSampleTimeWithoutFloatingPointArtifacts()
        {
            ProgressTestContext context = new();
            AudioProgress progress = new(context, context);
            progress.Report(10368, 48000, 0);
            Assert.Contains("time=00:00:00.216 ", Assert.Single(context.Messages), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.None)]
        public void QuietLogLevelsSuppressProgress(LogLevel level)
        {
            ProgressTestContext context = new() { MinimumLevel = level };
            AudioProgress progress = new(context, context);
            progress.Report(48000, 48000, 1024);
            progress.Report(48000, 48000, 1024, completed: true);
            Assert.Empty(context.Messages);
        }
    }
}
