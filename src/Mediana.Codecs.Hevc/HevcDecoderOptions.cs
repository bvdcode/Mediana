// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    /// <summary>Optional input limits. Output/reference pixels and motion storage use the supplied allocator.</summary>
    public record HevcDecoderOptions
    {
        public int MaxPicturePixels { get; init; } = int.MaxValue;
        public int MaxConfigurationBytes { get; init; } = int.MaxValue;
        public int MaxNalsPerPacket { get; init; } = int.MaxValue;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPicturePixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNalsPerPacket);
        }
    }
}
