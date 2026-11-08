// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;
using Xunit.Abstractions;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3PcmTests(ITestOutputHelper output)
    {
        [Theory]
        [InlineData("mono.ac3", "mono.pcm", 48000, 1, 60)]
        [InlineData("stereo.eac3", "stereo.pcm", 48000, 2, 60)]
        [InlineData("stereo-44100.ac3", "stereo-44100.pcm", 44100, 2, 60)]
        [InlineData("surround-32000.ac3", "surround-32000.pcm", 32000, 6, 60)]
        [InlineData("surround.eac3", "surround.pcm", 48000, 6, 60)]
        [InlineData("coupled.ac3", "coupled.pcm", 48000, 2, 60)]
        [InlineData("switched.ac3", "switched.pcm", 48000, 1, 135)]
        [InlineData("enhanced-switched.eac3", "enhanced-switched.pcm", 48000, 1, 135)]
        [InlineData("enhanced-one.eac3", "enhanced-one.pcm", 48000, 1, 135)]
        [InlineData("enhanced-two.eac3", "enhanced-two.pcm", 48000, 1, 135)]
        [InlineData("enhanced-three.eac3", "enhanced-three.pcm", 48000, 1, 135)]
        [InlineData("d25.ac3", "d25.pcm", 48000, 1, 135)]
        [InlineData("d45.ac3", "d45.pcm", 48000, 1, 135)]
        [InlineData("enhanced-d25.eac3", "enhanced-d25.pcm", 48000, 1, 135)]
        [InlineData("enhanced-d45.eac3", "enhanced-d45.pcm", 48000, 1, 135)]
        [InlineData("spectral.eac3", "spectral.pcm", 48000, 1, 130)]
        [InlineData("spectral-atten0.eac3", "spectral-atten0.pcm", 48000, 1, 130)]
        [InlineData("spectral-atten31.eac3", "spectral-atten31.pcm", 48000, 1, 130)]
        [InlineData("spectral-merged.eac3", "spectral-merged.pcm", 48000, 1, 130)]
        [InlineData("spectral-rematrix.eac3", "spectral-rematrix.pcm", 48000, 2, 130)]
        [InlineData("spectral-coupled.eac3", "spectral-coupled.pcm", 48000, 2, 130)]
        [InlineData("adaptive.eac3", "adaptive.pcm", 48000, 1, 100)]
        [InlineData("adaptive-switched.eac3", "adaptive-switched.pcm", 48000, 1, 100)]
        [InlineData("adaptive-quantizers.eac3", "adaptive-quantizers.pcm", 48000, 1, 135)]
        [InlineData("enhanced-coupling.eac3", "enhanced-coupling.pcm", 48000, 2, 135)]
        [InlineData("enhanced-coupling-merged.eac3", "enhanced-coupling-merged.pcm", 48000, 2, 135)]
        [InlineData("enhanced-coupling-spectral.eac3", "enhanced-coupling-spectral.pcm", 48000, 2, 135)]
        public async Task DecodedPcmMatchesIndependentReference(string encodedName, string referenceName, int rate, int channels, double minimumSnr)
        {
            (int SamplesPerChannel, double SignalToNoiseRatio, double RmsError, double MaximumError, long MaximumAllocatedBytes) metrics = await Ac3PcmTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), encodedName, referenceName, rate, channels, minimumSnr);
            output.WriteLine($"{encodedName}: {metrics.SamplesPerChannel} samples/channel; SNR {metrics.SignalToNoiseRatio:F6} dB; RMS error {metrics.RmsError:G9}; maximum error {metrics.MaximumError:G9}; maximum allocator bytes {metrics.MaximumAllocatedBytes}.");
        }
    }
}
