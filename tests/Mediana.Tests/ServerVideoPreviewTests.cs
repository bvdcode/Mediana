// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ServerVideoPreviewTests
    {
        [Fact]
        public Task CompleteHdPreviewSampleMatchesEveryIndependentSrgbPixel() =>
            ServerVideoPreviewTestInput.VerifyPngAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));

        [Theory]
        [InlineData("frame-rate-cfr.mp4")]
        [InlineData("frame-rate-vfr.mp4")]
        [InlineData("frame-rate-edited.mp4")]
        [InlineData("preview-hd.mp4")]
        public Task MidpointAndOutOfOrderPreviewsMatchIndependentPresentationTimeAndPixels(string name) =>
            ServerVideoPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name);
    }
}
