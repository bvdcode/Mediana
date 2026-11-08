// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AdtsEnhancementTransportTests
    {
        [Theory]
        [InlineData("AacSbr", "transients-mono", 1)]
        [InlineData("AacSbr", "transients-stereo", 2)]
        [InlineData("AacPs", "transients", 2)]
        public async Task RegroupingTransportPreservesEveryEnhancedSample(string directory, string name, int channels) =>
            await AdtsMultiBlockTestInput.VerifyEnhancementAsync(Path.Combine(AppContext.BaseDirectory, "Data", directory), name, channels);
    }
}
