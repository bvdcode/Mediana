// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AdtsDecodeTests
    {
        [Theory]
        [InlineData("lc-mono-48", 48000, 1, false)]
        [InlineData("lc-stereo-44", 44100, 2, false)]
        [InlineData("lc-mono-48", 48000, 1, true)]
        [InlineData("lc-stereo-44", 44100, 2, true)]
        public async Task IndependentAdtsFilesDecodeThroughThePublicEngine(string name, int rate, int channels, bool indexed) =>
            await AdtsDecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name, rate, channels, indexed);
    }
}
