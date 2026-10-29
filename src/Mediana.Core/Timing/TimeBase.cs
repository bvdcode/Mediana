// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Timing
{
    /// <summary>Positive seconds per tick. The default value is invalid.</summary>
    public readonly record struct TimeBase
    {
        public TimeBase(int numerator, int denominator)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
            int a = numerator;
            int b = denominator;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            Numerator = numerator / a;
            Denominator = denominator / a;
        }

        public int Numerator { get; }
        public int Denominator { get; }
        public static TimeBase TimeSpanTicks { get; } = new(1, (int)TimeSpan.TicksPerSecond);

        public void Validate()
        {
            if (Numerator <= 0 || Denominator <= 0)
            {
                throw new ArgumentException("A time base must contain positive, nonzero terms.");
            }
        }
    }
}
