// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Ogg
{
    public class OggReadOptions
    {
        public int MaximumPacketBytes { get; init; } = int.MaxValue;
        public int MaximumLogicalStreams { get; init; } = int.MaxValue;
        public bool VerifyChecksums { get; init; } = true;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPacketBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumLogicalStreams);
        }
    }
}
