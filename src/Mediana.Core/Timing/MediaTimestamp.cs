namespace Mediana.Core.Timing
{
    /// <summary>An exact timestamp. Use nullable timestamps for unknown time.</summary>
    public readonly struct MediaTimestamp : IEquatable<MediaTimestamp>, IComparable<MediaTimestamp>
    {
        public MediaTimestamp(long value, TimeBase timeBase)
        {
            timeBase.Validate();
            Value = value;
            TimeBase = timeBase;
        }

        public long Value { get; }
        public TimeBase TimeBase { get; }

        public MediaTimestamp Rescale(TimeBase target, TimestampRounding rounding)
        {
            TimeBase.Validate();
            target.Validate();
            Int128 numerator = (Int128)Value * TimeBase.Numerator * target.Denominator;
            Int128 denominator = (Int128)TimeBase.Denominator * target.Numerator;
            Int128 quotient = numerator / denominator;
            Int128 remainder = numerator % denominator;
            int sign = numerator.CompareTo(0);
            switch (rounding)
            {
                case TimestampRounding.TowardZero:
                    break;
                case TimestampRounding.Floor:
                    if (remainder < 0)
                    {
                        quotient--;
                    }
                    break;
                case TimestampRounding.Ceiling:
                    if (remainder > 0)
                    {
                        quotient++;
                    }
                    break;
                case TimestampRounding.NearestEven:
                    Int128 twiceRemainder = Int128.Abs(remainder) * 2;
                    if (twiceRemainder > denominator ||
                        (twiceRemainder == denominator && quotient % 2 != 0))
                    {
                        quotient += sign;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rounding));
            }
            return new MediaTimestamp(checked((long)quotient), target);
        }

        public int CompareTo(MediaTimestamp other)
        {
            TimeBase.Validate();
            other.TimeBase.Validate();
            Int128 left = (Int128)Value * TimeBase.Numerator * other.TimeBase.Denominator;
            Int128 right = (Int128)other.Value * other.TimeBase.Numerator * TimeBase.Denominator;
            return left.CompareTo(right);
        }

        public bool Equals(MediaTimestamp other)
        {
            if (TimeBase == default || other.TimeBase == default)
            {
                return Value == other.Value && TimeBase == other.TimeBase;
            }
            return CompareTo(other) == 0;
        }

        public override bool Equals(object? obj) => obj is MediaTimestamp other && Equals(other);

        public override int GetHashCode()
        {
            if (TimeBase == default)
            {
                return HashCode.Combine(Value, TimeBase);
            }
            Int128 numerator = (Int128)Value * TimeBase.Numerator;
            Int128 denominator = TimeBase.Denominator;
            Int128 a = Int128.Abs(numerator);
            Int128 b = denominator;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return HashCode.Combine(numerator / a, denominator / a);
        }

        public static bool operator ==(MediaTimestamp left, MediaTimestamp right) => left.Equals(right);
        public static bool operator !=(MediaTimestamp left, MediaTimestamp right) => !left.Equals(right);
    }
}
