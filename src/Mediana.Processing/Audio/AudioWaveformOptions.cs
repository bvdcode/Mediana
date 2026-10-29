// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Processing.Audio
{
    public record AudioWaveformOptions(int SampleRate, int SamplesPerWindow)
    {
        public AudioSampleFormat SampleFormat { get; init; } = AudioSampleFormat.Float64;
        public AudioResampleOptions Resampling { get; init; } = new();

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SampleRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SamplesPerWindow);
            ArgumentNullException.ThrowIfNull(Resampling);
            Resampling.Validate();
            switch (SampleFormat)
            {
                case AudioSampleFormat.Unsigned8:
                case AudioSampleFormat.Signed16:
                case AudioSampleFormat.Signed32:
                case AudioSampleFormat.Float32:
                case AudioSampleFormat.Float64:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(SampleFormat));
            }
        }
    }
}
