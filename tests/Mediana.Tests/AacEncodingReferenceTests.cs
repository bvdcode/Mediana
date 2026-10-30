// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacEncodingReferenceTests
    {
        [Theory]
        [InlineData("lc-mono-48", 64000, 19)]
        [InlineData("lc-stereo-44", 128000, 17)]
        [InlineData("lc-pns-48", 192000, 19)]
        public Task EveryEncodedSampleMatchesIndependentDecoding(string name, int bitRate, int packets) =>
            AacEncodingTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, bitRate, packets);
    }
}
