// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class SceneFilterPixelTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ColorMatricesMatchScalarPixels(bool linear, bool overwrite) => SceneFilterPixelTestInput.VerifyMatrices(linear, overwrite);

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ComponentTransfersMatchScalarPixels(bool linear, bool overwrite) => SceneFilterPixelTestInput.VerifyTransfer(linear, overwrite);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BoundedOffsetsMatchFullCanvasSampling(bool linear) => SceneFilterPixelTestInput.VerifyOffsets(linear);

        [Fact]
        public void FiltersHonorCancellation() => SceneFilterPixelTestInput.VerifyCancellation();
    }
}
