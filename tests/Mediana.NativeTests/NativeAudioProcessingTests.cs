// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAudioProcessingTests
    {
        public static async Task RunAsync()
        {
            foreach (AudioStorageLayout layout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
            {
                await AudioWindowTestInput.VerifyAsync(layout, 6, 100, 34, false);
                await AudioWindowTestInput.VerifyAsync(layout, 6, 17, 17, true);
                await AudioWindowTestInput.VerifyAsync(layout, 41, null, 0, false);
                await AudioWindowTestInput.VerifyAsync(layout, 100, 0, 0, true);
            }
            await AudioWindowTestInput.VerifySourceFailureAsync();
            Console.WriteLine("PASS audio windows: available sample intersection, exact clocks, source failures and storage ownership.");
            foreach (AudioSampleFormat sampleFormat in new[] { AudioSampleFormat.Unsigned8, AudioSampleFormat.Signed16,
                AudioSampleFormat.Signed32, AudioSampleFormat.Float32, AudioSampleFormat.Float64 })
            {
                AudioSamplePreparationTestInput.Verify(sampleFormat, AudioStorageLayout.Interleaved);
                AudioSamplePreparationTestInput.Verify(sampleFormat, AudioStorageLayout.Planar);
            }
            Console.WriteLine("PASS PCM preparation: matching sample storage, timing, original allocation budget and retained frame ownership.");
            await AudioWaveformTestInput.VerifyAsync();
            await WaveformMixerTestInput.VerifyAsync(AudioStorageLayout.Interleaved, 1);
            await WaveformMixerTestInput.VerifyAsync(AudioStorageLayout.Planar, 83);
            await AudioWaveformTestInput.VerifyWaveAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Wave"));
            foreach (string name in WaveformReferenceTestInput.Names)
            {
                await ServerAudioWaveformTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Waveform"), name);
            }
            Console.WriteLine("PASS audio waveform: explicit mono matrices, packet/layout invariance, PCM16 levels and exact window timing match.");
            await AudioResamplingTestInput.VerifyAsync(48000, 400, AudioStorageLayout.Interleaved);
            await AudioResamplingTestInput.VerifyAsync(44100, 400, AudioStorageLayout.Planar);
            await AudioResamplingTestInput.VerifyAsync(8000, 11025, AudioStorageLayout.Interleaved);
            Console.WriteLine("PASS audio resampling: passband tones, exact timing, channel separation and finite output ownership match.");
        }
    }
}
