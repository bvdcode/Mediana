// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Cli
{
    internal static class VideoRateOptions
    {
        private const long DefaultBufferMilliseconds = 2000;

        public static H264RateControlOptions? Create(int? bitRate, long? bufferMilliseconds)
        {
            if (bitRate is null)
            {
                if (bufferMilliseconds is not null)
                {
                    throw new ArgumentException("A video buffer duration requires a video bit rate.", nameof(bufferMilliseconds));
                }
                return null;
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitRate.Value);
            long milliseconds = bufferMilliseconds ?? DefaultBufferMilliseconds;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond);
            return new() { BitRate = bitRate.Value, BufferDuration = TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond) };
        }

        public static H264RateControlOptions?[]? CreateVariants(int[]? bitRates, long? bufferMilliseconds, int count)
        {
            if (bitRates is null)
            {
                Create(null, bufferMilliseconds);
                return null;
            }
            if (bitRates.Length != count)
            {
                throw new ArgumentException("Supply one --video-bit-rates value per --widths value, in the same order.", nameof(bitRates));
            }
            return bitRates.Select(rate => Create(rate, bufferMilliseconds)).ToArray();
        }
    }
}
