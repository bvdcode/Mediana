// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class PreviewIntegrationTests
    {
        [Fact]
        public async Task RotatedMp4ProducesAnOrientedPngMatchingIndependentColorAndGeometry()
        {
            await RotatedPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));
        }

        [Theory]
        [InlineData("avc-cabac-intra", 3, 5, 94, 62, 49)]
        [InlineData("avc-cabac-p0", 5, 10, 94, 62, 49)]
        [InlineData("avc-cabac-p1", 5, 10, 94, 62, 49)]
        [InlineData("avc-cabac-p2", 5, 10, 94, 62, 49)]
        [InlineData("avc-cavlc-8x8-weighted", 6, 12, 190, 126, 50)]
        [InlineData("avc-cabac-8x8-2", 6, 12, 190, 126, 50)]
        public async Task AvcMp4SelectsTheNextPresentationTimeAndProducesAnIndependentSrgbPng(string name, int frameIndex,
            int fps, int width, int height, int previewHeight)
        {
            await SdrPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, frameIndex, fps, width, height, previewHeight);
        }
    }
}
