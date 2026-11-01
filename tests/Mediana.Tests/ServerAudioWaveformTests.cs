// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ServerAudioWaveformTests
    {
        [Theory]
        [InlineData("silence")]
        [InlineData("impulses")]
        [InlineData("music")]
        [InlineData("speech")]
        [InlineData("fractional")]
        public Task PublicSampleMatchesCompleteIndependentWaveform(string name) =>
            ServerAudioWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Waveform"), name);
    }
}
