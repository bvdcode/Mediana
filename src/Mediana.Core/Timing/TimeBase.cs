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

        /// <summary>Returns a unit-numerator clock that represents every supplied tick exactly.
        /// Fails when their least common denominator exceeds the supported signed 32-bit clock range.</summary>
        public static TimeBase Common(params ReadOnlySpan<TimeBase> clocks)
        {
            if (clocks.IsEmpty) { throw new ArgumentException("Supply at least one media clock.", nameof(clocks)); }
            int denominator = 1;
            foreach (TimeBase clock in clocks)
            {
                clock.Validate();
                int a = denominator;
                int b = clock.Denominator;
                while (b != 0) { (a, b) = (b, a % b); }
                long combined = (long)(denominator / a) * clock.Denominator;
                if (combined > int.MaxValue) { throw new NotSupportedException("The exact common media clock exceeds its signed 32-bit denominator."); }
                denominator = (int)combined;
            }
            return new(1, denominator);
        }

        public void Validate()
        {
            if (Numerator <= 0 || Denominator <= 0)
            {
                throw new ArgumentException("A time base must contain positive, nonzero terms.");
            }
        }
    }
}
