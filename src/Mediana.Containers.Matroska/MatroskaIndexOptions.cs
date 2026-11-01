// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaIndexOptions(int MaximumIndexBytes, int MaximumBlocks,
        long MaximumPacketsPerTrack, int MaximumBlockHeaderBytes, int MaximumFrameBytes, bool VerifyClusterChecksums)
    {
        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumIndexBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumBlocks);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPacketsPerTrack);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumBlockHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFrameBytes);
        }
    }
}
