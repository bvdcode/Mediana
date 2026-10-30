// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    public record MediaOpenOptions
    {
        public long BufferBudgetBytes { get; init; } = 64 * 1024 * 1024;
        public int MaxProbeBytes { get; init; } = 64 * 1024;
        /// <summary>Optional total container read budget, including skipped packets and EOF checks.</summary>
        public long? MaxPacketReads { get; init; }
        /// <summary>Optional total session read budget, including timestamp selection and EOF checks.</summary>
        public long? MaxFrameReads { get; init; }

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BufferBudgetBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxProbeBytes);
            if (MaxPacketReads is long packets)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packets);
            }
            if (MaxFrameReads is long frames)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
            }
        }
    }
}
