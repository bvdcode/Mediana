// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltDeemphasis
    {
        private const float Coefficient = .85000610f;
        private const float Scale = 1f / 32768;
        private const float Bias = 1e-30f;

        public static void Apply(ReadOnlySpan<float> planar, int samples, int channels, int stride,
            Span<float> output, Span<float> memory, bool accumulate = false)
        {
            if (channels is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(channels)); }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
            ArgumentOutOfRangeException.ThrowIfLessThan(stride, samples);
            ArgumentOutOfRangeException.ThrowIfLessThan(planar.Length, (channels - 1) * stride + samples, nameof(planar));
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, samples * channels, nameof(output));
            ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, channels, nameof(memory));
            if (planar.Overlaps(output) || planar.Overlaps(memory) || output.Overlaps(memory))
            {
                throw new ArgumentException("CELT de-emphasis input, output and memory must not overlap.");
            }
            for (int channel = 0; channel < channels; channel++)
            {
                float previous = memory[channel];
                ReadOnlySpan<float> input = planar.Slice(channel * stride, samples);
                for (int at = 0; at < samples; at++)
                {
                    float sample = input[at] + Bias + previous;
                    previous = Coefficient * sample;
                    int index = at * channels + channel;
                    if (accumulate) { output[index] += sample * Scale; }
                    else { output[index] = sample * Scale; }
                }
                memory[channel] = previous;
            }
        }
    }
}
