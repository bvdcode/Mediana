// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class AacRealDecodeTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        [Theory]
        [InlineData("lc-mono-48", false)]
        [InlineData("lc-stereo-44", false)]
        [InlineData("lc-mono-48", true)]
        [InlineData("lc-stereo-44", true)]
        public async Task ExplicitLeadingOrCompleteEditTrimmingMatchesIndependentPcm(string name, bool exactEdit)
        {
            await AacEditedAudioTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, exactEdit);
        }

        [Theory]
        [InlineData("lc-mono-48", false)]
        [InlineData("lc-stereo-44", false)]
        [InlineData("lc-mono-48", true)]
        [InlineData("lc-stereo-44", true)]
        public async Task WaveformMatchesProcessingOfIndependentDecodedPcm(string name, bool trimToEdit)
        {
            await AacWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), name, trimToEdit);
        }

        [Theory]
        [InlineData("lc-mono-48")]
        [InlineData("lc-stereo-44")]
        [InlineData("lc-pns-48")]
        public async Task CompleteRealSequencesMatchIndependentFloatPcmOrNoiseEnergy(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Aac");
            await AacDecodeTestInput.VerifyAsync(root, name);
            (int sequences, int shapes, int books, int filters, int midSideBands) = await AacFeatureTestInput.VerifyAsync(root, name);
            output.WriteLine($"{name}: sequences {sequences:X}, shapes {shapes:X}, books {books:X}, TNS filters {filters}, M/S bands {midSideBands}.");
            Assert.NotEqual(0, sequences);
            Assert.Equal(name == "lc-pns-48", (books & (1 << 13)) != 0);
        }
    }
}
