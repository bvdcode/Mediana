// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    /// <summary>Per-frame raw AAC bit budget without a bit reservoir or constant-rate padding.</summary>
    public record AacEncoderOptions
    {
        public int BitRate { get; init; } = 128000;
        public int MaximumInputSamplesPerChannel { get; init; } = 65536;

        internal void Validate(int sampleRate, int channels)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BitRate);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumInputSamplesPerChannel);
            if ((long)BitRate * 1024 / sampleRate < 64 + channels * 16 ||
                (long)BitRate * 1024 > 6144L * channels * sampleRate)
            {
                throw new ArgumentOutOfRangeException(nameof(BitRate), "The AAC frame budget must fit the channel syntax and the 6144-bit per-channel buffer limit.");
            }
        }
    }
}
