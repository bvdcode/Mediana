// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    /// <summary>Codec history required between an independently decodable restart and the first requested output packet.</summary>
    public readonly record struct RandomAccessRecovery
    {
        public RandomAccessRecovery(int requiredPrerollPackets)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(requiredPrerollPackets);
            RequiredPrerollPackets = requiredPrerollPackets;
        }

        public int RequiredPrerollPackets { get; }
    }
}
