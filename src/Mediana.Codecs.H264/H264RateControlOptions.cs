// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Constrained variable packet rate without filler or HRD timing messages.</summary>
    public record H264RateControlOptions
    {
        private const int MaximumBufferBits = 62500000;
        public int BitRate { get; init; } = 2000000;
        public TimeSpan BufferDuration { get; init; } = TimeSpan.FromSeconds(2);

        internal int BufferBits => (int)((Int128)BitRate * BufferDuration.Ticks / TimeSpan.TicksPerSecond);

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BitRate);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(BitRate, H264EncoderConfiguration.MaximumBitsPerSecond);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(BufferDuration, TimeSpan.Zero);
            Int128 bits = (Int128)BitRate * BufferDuration.Ticks / TimeSpan.TicksPerSecond;
            if (bits < 64 || bits > MaximumBufferBits)
            {
                throw new ArgumentOutOfRangeException(nameof(BufferDuration), "The H.264 packet buffer must contain 64 through 62500000 bits.");
            }
        }
    }
}
