// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class EditedVideoHlsTests
    {
        [Theory]
        [InlineData(2, 1)]
        [InlineData(1, 5)]
        public async Task RealEditedMoviePlansOnlyPresentedPicturesAndSupportsColdReverseRequests(int ticks, int frequency) =>
            await OnDemandHlsTestInput.VerifyEditedAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), ticks, frequency);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FractionalEditBoundariesTrimSourcePicturesBeforeStrictHlsAlignment(bool wide) =>
            await OnDemandHlsTestInput.VerifyFractionalEditsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), wide);
    }
}
