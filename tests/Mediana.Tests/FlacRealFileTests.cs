// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class FlacRealFileTests
    {
        [Theory]
        [InlineData("stereo-covers")]
        [InlineData("mono-speech")]
        public async Task CompleteWaveformMatchesTheOriginalPcmSource(string name)
        {
            await FlacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), name);
        }

        [Theory]
        [InlineData("stereo-covers", 1)]
        [InlineData("mono-speech", 127)]
        [InlineData("stereo24", 127)]
        [InlineData("surround24", 16384)]
        [InlineData("stereo32", 1)]
        public async Task AllPcmSamplesMatchIndependentReferences(string name, int packetBytes)
        {
            await FlacDecodeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), name, packetBytes);
        }

        [Theory]
        [InlineData(127)]
        [InlineData(16384)]
        public async Task RealMetadataPicturesAndCompressedBytesMatchIndependentReferences(int packetBytes)
        {
            await FlacMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Flac"), packetBytes);
        }
    }
}
