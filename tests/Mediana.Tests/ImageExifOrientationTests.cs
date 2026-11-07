// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ImageExifOrientationTests
    {
        [Fact]
        public Task PngOrientationsPreserveCompletePixelsAcrossDepthsAdam7AndReduction() =>
            ImageExifOrientationTestInput.VerifyPngAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png"));

        [Fact]
        public Task WebpOrientationsApplyAfterCompositingAndPreserveIndependentFrames() =>
            ImageExifOrientationTestInput.VerifyWebpAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Raster"));

        [Fact]
        public void InvalidOrientationsFailBeforeAllocationAndInverseMappingIsExact() =>
            ImageExifOrientationTestInput.VerifyInvalidMetadata();
    }
}
