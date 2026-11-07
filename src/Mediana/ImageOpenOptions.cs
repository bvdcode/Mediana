// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    /// <summary>Bounds encoded image input and recognition; codec workspace limits belong to each registered module.</summary>
    public record ImageOpenOptions
    {
        /// <summary>Explicitly selects a registered implementation. Null recognizes content automatically.
        /// Selected decoders validate the input; recognition failure does not select another implementation.</summary>
        public string? DecoderImplementationId { get; init; }
        public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
        public int MaxProbeBytes { get; init; } = 64 * 1024;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxProbeBytes);
        }
    }
}
