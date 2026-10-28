using System.Numerics;

namespace Mediana.Core.Video
{
    /// <summary>Width of one sample relative to its height. The default value is invalid.</summary>
    public readonly record struct SampleAspectRatio
    {
        public SampleAspectRatio(int numerator, int denominator)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
            int divisor = (int)BigInteger.GreatestCommonDivisor(numerator, denominator);
            Numerator = numerator / divisor;
            Denominator = denominator / divisor;
        }

        public int Numerator { get; }
        public int Denominator { get; }
        public static SampleAspectRatio Square { get; } = new(1, 1);

        public void Validate()
        {
            if (Numerator <= 0 || Denominator <= 0)
            {
                throw new ArgumentException("A sample aspect ratio must contain positive, nonzero terms.");
            }
        }
    }
}
