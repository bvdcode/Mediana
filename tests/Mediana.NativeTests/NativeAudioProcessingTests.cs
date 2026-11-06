// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAudioProcessingTests
    {
        public static async Task RunAsync()
        {
            foreach (AudioSampleFormat sampleFormat in new[] { AudioSampleFormat.Unsigned8, AudioSampleFormat.Signed16,
                AudioSampleFormat.Signed32, AudioSampleFormat.Float32, AudioSampleFormat.Float64 })
            {
                PcmStorageTestInput.Verify(sampleFormat);
            }
            Console.WriteLine("PASS native PCM storage: independent packet/frame leases, bounded slices and original allocation budgets.");
            foreach (AudioStorageLayout layout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
            {
                await AudioWindowTestInput.VerifyAsync(layout, 6, 100, 34, false);
                await AudioWindowTestInput.VerifyAsync(layout, 6, 17, 17, true);
                await AudioWindowTestInput.VerifyAsync(layout, 41, null, 0, false);
                await AudioWindowTestInput.VerifyAsync(layout, 100, 0, 0, true);
                await AudioWindowTestInput.VerifyWholeBlocksAsync(layout, false);
                await AudioWindowTestInput.VerifyWholeBlocksAsync(layout, true);
                await AudioTimeWindowTestInput.VerifyAsync(layout, 44100, new(1, new(1, 7)));
                await AudioTimeWindowTestInput.VerifyAsync(layout, 48000, new(-7, new(1, 48000)));
            }
            await AudioWindowTestInput.VerifySourceFailureAsync();
            await PacketMergeTestInput.VerifyAsync();
            await AacEditedAudioTestInput.VerifyTimeWindowAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-mono-48");
            await AacEditedAudioTestInput.VerifyTimeWindowAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-stereo-44");
            await AacEditedAudioTestInput.VerifyTimeWindowAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-mono-48", indexed: true);
            await AacEditedAudioTestInput.VerifyTimeWindowAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"), "lc-stereo-44", indexed: true);
            Console.WriteLine("PASS audio windows: available sample intersection, exact clocks, source failures and storage ownership.");
            foreach (AudioSampleFormat sampleFormat in new[] { AudioSampleFormat.Unsigned8, AudioSampleFormat.Signed16,
                AudioSampleFormat.Signed32, AudioSampleFormat.Float32, AudioSampleFormat.Float64 })
            {
                AudioSamplePreparationTestInput.Verify(sampleFormat, AudioStorageLayout.Interleaved);
                AudioSamplePreparationTestInput.Verify(sampleFormat, AudioStorageLayout.Planar);
                AudioFrameTimingTestInput.Verify(sampleFormat, AudioStorageLayout.Interleaved);
                AudioFrameTimingTestInput.Verify(sampleFormat, AudioStorageLayout.Planar);
                await PcmStreamOutputTestInput.VerifyAsync(sampleFormat, true);
                await PcmStreamOutputTestInput.VerifyAsync(sampleFormat, false);
            }
            await PcmStreamOutputTestInput.VerifyPendingFailureAsync(false, false);
            await PcmStreamOutputTestInput.VerifyPendingFailureAsync(false, true);
            await PcmStreamOutputTestInput.VerifyPendingFailureAsync(true, false);
            await PcmStreamOutputTestInput.VerifyPendingFailureAsync(true, true);
            Console.WriteLine("PASS PCM stream output: both byte orders, direct storage, bounded scratch and asynchronous failure ownership.");
            Console.WriteLine("PASS PCM preparation: matching sample storage, timing, original allocation budget and retained frame ownership.");
            await Mp4RecoveryTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"));
            foreach ((string directory, string name) in new[] { ("Aac", "lc-mono-48"), ("Aac", "lc-stereo-44"),
                ("AacMultichannel", "layout-6"), ("AacMultichannel", "window-transitions") })
            {
                await AacSeekTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", directory), name, Mp4TimestampMode.Media);
                await AacSeekTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", directory), name, Mp4TimestampMode.LinearMovie);
            }
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
