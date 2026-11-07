// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class VideoTranscodeTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");

        [Fact]
        public Task AdaptiveVp9ConvenienceUsesTheSamePreparationClockAndOutputAsProcessing() =>
            VideoTranscodeTestInput.VerifyAdaptiveAsync(Root);

        [Theory]
        [InlineData(1001, false)]
        [InlineData(1101, false)]
        [InlineData(1001, true)]
        public Task FractionalMovieTailDerivesAnExactClockInsteadOfRoundingToTheStreamClock(int milliseconds, bool rateControl) =>
            VideoTranscodeTestInput.VerifyFractionalTailAsync(Root, milliseconds, rateControl);
    }
}
