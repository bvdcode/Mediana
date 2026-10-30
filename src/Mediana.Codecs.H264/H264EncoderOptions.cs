// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    public record H264EncoderOptions
    {
        public int Quantizer { get; init; } = 26;
        public int MaximumPacketBytes { get; init; } = 16 * 1024 * 1024;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(Quantizer);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Quantizer, 51);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPacketBytes, 8);
        }
    }
}
