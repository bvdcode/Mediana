// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Flac
{
    public record FlacReadOptions
    {
        public int PacketBytes { get; init; } = 16 * 1024;
        public int MaxMetadataBlocks { get; init; } = 1024;
        public long MaxMetadataBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxTextBytes { get; init; } = 1024 * 1024;
        public int MaxComments { get; init; } = 4096;
        public int MaxPictures { get; init; } = 64;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PacketBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBlocks);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTextBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxComments);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPictures);
        }
    }
}
