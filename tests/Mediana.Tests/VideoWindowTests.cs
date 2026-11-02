// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class VideoWindowTests
    {
        [Fact]
        public Task WindowsTransferOriginalFramesWithoutAllocatingPixelStorage() => VideoWindowTestInput.VerifySelectionAsync();

        [Fact]
        public Task InvalidTimingAndSourceFailuresReleaseFramesAndBecomeTerminal() => VideoWindowTestInput.VerifyFailureAsync();

        [Fact]
        public Task CancellationReleasesReceivedFramesAndStopsReading() => VideoWindowTestInput.VerifyCancellationAsync();

        [Fact]
        public Task ConcurrentReadAndDisposeCannotInvalidateOwnedOutput() => VideoWindowTestInput.VerifyConcurrencyAsync();

        [Theory]
        [InlineData("frame-rate-cfr.mp4")]
        [InlineData("frame-rate-vfr.mp4")]
        [InlineData("frame-rate-edited.mp4")]
        [InlineData("preview-hd.mp4")]
        public Task OutOfOrderWindowsMatchCompleteIndependentPixelsAndMovieTimes(string name) =>
            RealVideoWindowTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name);
    }
}
