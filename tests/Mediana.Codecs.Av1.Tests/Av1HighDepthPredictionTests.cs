// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1HighDepthPredictionTests
    {
        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        public void NativeSamplesAndClippedDestinationsMatchEveryPredictionGeometry(int bitDepth) =>
            Av1HighDepthPredictionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), bitDepth);

        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        public void NativeFilteredAndUpsampledEdgesMatchEveryDirectionAndAvailability(int bitDepth) =>
            Av1HighDepthPredictionTestInput.VerifyDirections(Path.Combine(AppContext.BaseDirectory, "Data"), bitDepth);
    }
}
