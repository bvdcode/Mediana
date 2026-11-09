// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>In-loop filtering of reconstructed pictures before presentation and reference prediction.</summary>
    public record H264DeblockingOptions
    {
        /// <summary>Alpha and clipping threshold offset, from -6 through 6. Each unit adjusts the filter table index by two.</summary>
        public int AlphaOffset { get; init; }
        /// <summary>Beta threshold offset, from -6 through 6. Each unit adjusts the filter table index by two.</summary>
        public int BetaOffset { get; init; }

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(AlphaOffset, -6);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(AlphaOffset, 6);
            ArgumentOutOfRangeException.ThrowIfLessThan(BetaOffset, -6);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(BetaOffset, 6);
        }
    }
}
