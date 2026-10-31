// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;
using Mediana.Containers;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliFrameRateTests
    {
        [Theory]
        [InlineData("frame-rate-cfr.mp4", "30000/1001 (29.97 fps)")]
        [InlineData("frame-rate-vfr.mp4", "900000/32213 (27.939 fps)")]
        [InlineData("frame-rate-edited.mp4", "2070000/84257 (24.568 fps)")]
        public async Task ProbePrintsTheExactVideoAverageOnceWithoutAnAudioFrameRate(string file, string expected)
        {
            string input = Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate", file);
            (int code, string output, string error) = await CliTests.RunAsync(["-i", input]);
            Assert.True(code == 0, error);
            Assert.Contains("Average frame rate: " + expected + ".", output, StringComparison.Ordinal);
            Assert.Equal(1, output.Split('\n').Count(line => line.Contains("Average frame rate:", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task GenericFormatterPreservesFractionAndInvariantDecimalWithoutInventingUnknownRates()
        {
            using StringWriter output = new(CultureInfo.GetCultureInfo("fr-FR"));
            await ContainerStreamInfoFormatter.WriteAsync(new(0, averageFrameRate: new(60000, 2002)), output, default);
            Assert.Equal("  Average frame rate: 30000/1001 (29.97 fps)." + Environment.NewLine, output.ToString());
            using StringWriter unknown = new(CultureInfo.InvariantCulture);
            await ContainerStreamInfoFormatter.WriteAsync(new(0), unknown, default);
            Assert.Empty(unknown.ToString());
        }
    }
}
