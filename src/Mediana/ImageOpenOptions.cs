// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    /// <summary>Bounds encoded image input and recognition; codec workspace limits belong to each registered module.</summary>
    public record ImageOpenOptions
    {
        public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
        public int MaxProbeBytes { get; init; } = 64 * 1024;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxProbeBytes);
        }
    }
}
