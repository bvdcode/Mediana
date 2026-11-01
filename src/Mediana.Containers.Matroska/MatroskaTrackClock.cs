// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    /// <summary>Maps native block clocks to nearest-even nanoseconds without floating-point timestamp arithmetic.
    /// An offset represents known contiguous decoded time after the first frame of a block.</summary>
    internal class MatroskaTrackClock
    {
        private const long NanosecondsPerSecond = 1000000000;
        private readonly ulong segmentScale;
        private readonly ulong codecDelay;
        private readonly bool unitTrackScale;
        private readonly BigInteger trackTickNumerator;
        private readonly BigInteger trackTickDenominator;

        public MatroskaTrackClock(ulong timestampScale, MatroskaTrackTiming timing)
        {
            ArgumentOutOfRangeException.ThrowIfZero(timestampScale);
            if (!double.IsFinite(timing.TimestampScale) || timing.TimestampScale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timing));
            }
            segmentScale = timestampScale;
            codecDelay = timing.CodecDelayNanoseconds;
            unitTrackScale = timing.TimestampScale == 1;
            ulong bits = BitConverter.DoubleToUInt64Bits(timing.TimestampScale);
            int exponent = (int)(bits >> 52);
            ulong significand = bits & ((1UL << 52) - 1);
            int shift;
            if (exponent == 0)
            {
                shift = -1074;
            }
            else
            {
                significand |= 1UL << 52;
                shift = exponent - 1075;
            }
            BigInteger numerator = (BigInteger)significand * timestampScale;
            BigInteger denominator = BigInteger.One;
            if (shift >= 0)
            {
                numerator <<= shift;
            }
            else
            {
                denominator <<= -shift;
            }
            BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            trackTickNumerator = numerator / divisor;
            trackTickDenominator = denominator / divisor;
        }

        public static TimeBase TimeBase { get; } = new(1, (int)NanosecondsPerSecond);

        public MediaTimestamp GetPresentationTime(ulong clusterTimestamp, short relativeTimestamp,
            MediaDuration? contiguousOffset = null)
        {
            contiguousOffset?.TimeBase.Validate();
            if (unitTrackScale)
            {
                Int128 ticks = (Int128)clusterTimestamp + relativeTimestamp;
                if (ticks > Int128.MaxValue / segmentScale)
                {
                    throw OutOfRange();
                }
                Int128 whole = ticks * segmentScale - codecDelay;
                if (whole > long.MaxValue)
                {
                    throw OutOfRange();
                }
                Int128 numerator = whole;
                Int128 denominator = 1;
                if (contiguousOffset is MediaDuration offset)
                {
                    denominator = offset.TimeBase.Denominator;
                    numerator = whole * denominator + (Int128)offset.Value * offset.TimeBase.Numerator * NanosecondsPerSecond;
                }
                return new(Round(numerator, denominator), TimeBase);
            }
            BigInteger wholeNs = (BigInteger)clusterTimestamp * segmentScale - codecDelay;
            BigInteger scaled = wholeNs * trackTickDenominator + relativeTimestamp * trackTickNumerator;
            BigInteger divisor = trackTickDenominator;
            if (contiguousOffset is MediaDuration duration)
            {
                scaled = scaled * duration.TimeBase.Denominator +
                    (BigInteger)duration.Value * duration.TimeBase.Numerator * NanosecondsPerSecond * divisor;
                divisor *= duration.TimeBase.Denominator;
            }
            return new(Round(scaled, divisor), TimeBase);
        }

        public MediaDuration GetTrackDuration(ulong ticks)
        {
            if (unitTrackScale)
            {
                if (ticks > (ulong)long.MaxValue / segmentScale)
                {
                    throw OutOfRange();
                }
                return new((long)(ticks * segmentScale), TimeBase);
            }
            return new(Round(ticks * trackTickNumerator, trackTickDenominator), TimeBase);
        }

        private static long Round<T>(T numerator, T denominator) where T : IBinaryInteger<T>
        {
            T quotient = numerator / denominator;
            T remainder = T.Abs(numerator % denominator) * T.CreateChecked(2);
            if (remainder > denominator || (remainder == denominator && !T.IsEvenInteger(quotient)))
            {
                quotient += T.CreateChecked(T.Sign(numerator));
            }
            if (quotient < T.CreateChecked(long.MinValue) || quotient > T.CreateChecked(long.MaxValue))
            {
                throw OutOfRange();
            }
            return long.CreateChecked(quotient);
        }

        private static NotSupportedException OutOfRange() => new("The Matroska timestamp exceeds the supported nanosecond range.");
    }
}
