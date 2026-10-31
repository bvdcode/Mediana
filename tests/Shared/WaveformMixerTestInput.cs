// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class WaveformMixerTestInput
    {
        public static async Task VerifyAsync(AudioStorageLayout layout, int blockSize)
        {
            const int samples = 83;
            const int window = 7;
            MediaBufferAllocator allocator = new(16384);
            AudioSignalSource source = new(allocator, 400, samples, Signal, [blockSize], layout,
                ["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right"]);
            AudioChannelMixer mixer = AudioDownmix.CreateMono(source.Format);
            await using AudioWaveformReader reader = new(source.ReadAsync, mixer,
                new AudioWaveformOptions(400, window) { SampleFormat = AudioSampleFormat.Signed16 }, allocator);
            int position = 0;
            while (await reader.ReadLevelAsync() is AudioLevel level)
            {
                int count = Math.Min(window, samples - position);
                double energy = 0;
                double peak = 0;
                for (int index = position; index < position + count; index++)
                {
                    double center = Math.Sqrt(0.5);
                    double mixed = (center * Signal(index, 0) + center * Signal(index, 1)
                        + Signal(index, 2) + 0.5 * Signal(index, 4) + 0.5 * Signal(index, 5))
                        / (2 * center + 2);
                    double quantized = Math.Round(mixed * 32768) / 32768;
                    energy += quantized * quantized;
                    peak = Math.Max(peak, Math.Abs(quantized));
                }
                if (level.StartSample != position || level.SampleCount != count
                    || level.Timestamp != new MediaTimestamp(position - 7, new TimeBase(1, 400))
                    || level.Duration != new MediaDuration(count, new TimeBase(1, 400))
                    || Math.Abs(level.RootMeanSquare - Math.Sqrt(energy / count)) > 1e-15
                    || level.Peak != peak)
                {
                    throw new InvalidDataException("The supplied waveform mixer changed mono samples, window boundaries or timing.");
                }
                position += count;
            }
            if (position != samples || allocator.AllocatedBytes != 0 || await reader.ReadLevelAsync() is not null)
            {
                throw new InvalidDataException("The supplied waveform mixer lost the final samples or retained media storage.");
            }
        }

        private static double Signal(long index, int channel) => ((index * 3 + channel * 7) % 19 - 9) / 32d;
    }
}
