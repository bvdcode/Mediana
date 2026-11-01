// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Core.Video
{
    /// <summary>Exact positive coded-picture count per second. Does not assert constant cadence.
    /// The default value is invalid.</summary>
    public readonly record struct FrameRate
    {
        public FrameRate(Int128 numerator, Int128 denominator)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
            Int128 a = numerator;
            Int128 b = denominator;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            Numerator = numerator / a;
            Denominator = denominator / a;
        }

        public Int128 Numerator { get; }
        public Int128 Denominator { get; }

        public void Validate()
        {
            if (Numerator <= 0 || Denominator <= 0)
            {
                throw new ArgumentException("A frame rate must contain positive, nonzero terms.");
            }
        }

        /// <summary>Returns no rate for an empty count or zero interval. The interval's source and scope belong to the caller.</summary>
        public static FrameRate? FromFrameCount(long frameCount, MediaDuration interval)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
            interval.TimeBase.Validate();
            if (frameCount == 0 || interval.Value == 0)
            {
                return null;
            }
            return new FrameRate((Int128)frameCount * interval.TimeBase.Denominator,
                (Int128)interval.Value * interval.TimeBase.Numerator);
        }

        public double ToDouble()
        {
            Validate();
            return (double)Numerator / (double)Denominator;
        }
    }
}
