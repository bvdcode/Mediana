// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264MultiSliceTests
    {
        [Theory]
        [InlineData("avc-multislice-cavlc", 5)]
        [InlineData("avc-multislice-cabac", 5)]
        [InlineData("avc-multislice-cabac-sliced", 3)]
        public async Task EveryReconstructedAndPublicSessionPixelMatchesIndependentDecoding(string name, int slices) =>
            await H264MultiSliceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, slices);
    }
}
