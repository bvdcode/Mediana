// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Wave
{
    public record WaveReadOptions
    {
        public int SamplesPerPacket { get; init; } = 4096;
        public int MaxFormatBytes { get; init; } = 4096;
        public int MaxChunks { get; init; } = 4096;
        public long MaxMetadataBytes { get; init; } = 16 * 1024 * 1024;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SamplesPerPacket);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxFormatBytes, 16);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxChunks);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBytes);
        }
    }
}
