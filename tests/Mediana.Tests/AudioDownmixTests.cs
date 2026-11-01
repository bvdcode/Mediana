// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioDownmixTests
    {
        [Theory]
        [InlineData(false, AudioStorageLayout.Interleaved)]
        [InlineData(false, AudioStorageLayout.Planar)]
        [InlineData(true, AudioStorageLayout.Interleaved)]
        [InlineData(true, AudioStorageLayout.Planar)]
        public void EveryShuffledSpeakerAndCorrelatedPeakPreserveMatrixTimingAndStorage(bool stereo, AudioStorageLayout layout) =>
            AudioDownmixTestInput.Verify(stereo, layout);

        [Theory]
        [InlineData(false, AudioStorageLayout.Interleaved)]
        [InlineData(false, AudioStorageLayout.Planar)]
        [InlineData(true, AudioStorageLayout.Interleaved)]
        [InlineData(true, AudioStorageLayout.Planar)]
        public void SinglePrecisionOutputPreservesEverySpeakerAndPeak(bool stereo, AudioStorageLayout layout) =>
            AudioDownmixTestInput.Verify(stereo, layout, AudioSampleFormat.Float32);

        [Fact]
        public void CommonNormalizationPreservesAsymmetricStereoBalance() => AudioDownmixTestInput.VerifyBalance();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SideSurroundSpeakersHaveTheSameContributions(bool stereo) =>
            AudioDownmixTestInput.Verify(stereo, AudioStorageLayout.Planar, AudioSampleFormat.Float32, sideSurround: true);

        [Theory]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        public void IntegerMixingRequiresExplicitSampleConversion(AudioSampleFormat sampleFormat)
        {
            AudioFormat input = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "front-center");
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioDownmix.CreateMono(input, sampleFormat));
        }

        [Theory]
        [InlineData("front-left-of-center")]
        [InlineData("back-center")]
        [InlineData("channel-1")]
        public void UndefinedSpeakersRequireExplicitMatrix(string speaker)
        {
            AudioFormat input = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, speaker);
            Assert.Throws<NotSupportedException>(() => AudioDownmix.CreateMono(input));
            Assert.Throws<NotSupportedException>(() => AudioDownmix.CreateStereo(input));
        }

        [Fact]
        public void DuplicateSurroundRolesRequireExplicitMatrix()
        {
            AudioFormat input = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "side-left", "back-left");
            Assert.Throws<NotSupportedException>(() => AudioDownmix.CreateStereo(input));
        }
    }
}
