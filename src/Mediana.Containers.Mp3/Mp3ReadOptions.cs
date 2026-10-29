// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp3
{
    public record Mp3ReadOptions
    {
        public int MaxMetadataBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxTextBytes { get; init; } = 1024 * 1024;
        public int MaxMetadataFrames { get; init; } = 4096;
        public int MaxPictures { get; init; } = 64;
        public int MaxFrameBytes { get; init; } = 4096;
        public long MaxAudioFrames { get; init; } = 10_000_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTextBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataFrames);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPictures);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxFrameBytes, 4);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAudioFrames);
        }
    }
}
