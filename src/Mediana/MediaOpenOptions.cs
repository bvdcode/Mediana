// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    public record MediaOpenOptions
    {
        public long BufferBudgetBytes { get; init; } = 64 * 1024 * 1024;
        public int MaxProbeBytes { get; init; } = 64 * 1024;
        public long MaxPacketReads { get; init; } = 1_000_000;
        public long MaxFrameReads { get; init; } = 100_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BufferBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxProbeBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPacketReads);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFrameReads);
        }
    }
}
