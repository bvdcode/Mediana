// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class WaveformReferenceTests
    {
        [Theory]
        [InlineData("silence", 127)]
        [InlineData("impulses", 127)]
        [InlineData("music", 127)]
        [InlineData("speech", 127)]
        [InlineData("fractional", 127)]
        [InlineData("silence", 1024)]
        [InlineData("impulses", 1024)]
        [InlineData("music", 1024)]
        [InlineData("speech", 1024)]
        [InlineData("fractional", 1024)]
        public async Task StreamingPcmAndLevelsMatchIndependentServerReferences(string name, int packetSize)
        {
            await WaveformReferenceTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Waveform"), name, packetSize);
        }
    }
}
