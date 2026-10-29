namespace Mediana.Codecs.Aac
{
    internal static class AacWindows
    {
        private static readonly double[] LongSine = Create(1024, false);
        private static readonly double[] ShortSine = Create(128, false);
        private static readonly double[] LongKbd = Create(1024, true);
        private static readonly double[] ShortKbd = Create(128, true);

        public static ReadOnlySpan<double> Get(int length, int shape) => (length, shape) switch
        {
            (1024, 0) => LongSine,
            (128, 0) => ShortSine,
            (1024, 1) => LongKbd,
            (128, 1) => ShortKbd,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        private static double[] Create(int length, bool kbd)
        {
            double[] result = new double[length];
            if (!kbd)
            {
                for (int index = 0; index < length; index++)
                {
                    result[index] = Math.Sin(Math.PI / (2 * length) * (index + 0.5));
                }
                return result;
            }
            double sum = 0;
            double alpha = length switch
            {
                1024 => 4,
                128 => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(length))
            };
            for (int index = 0; index <= length; index++)
            {
                double position = 2.0 * index / length - 1;
                sum += Bessel(Math.PI * alpha * Math.Sqrt(Math.Max(0, 1 - position * position)));
                if (index < length)
                {
                    result[index] = sum;
                }
            }
            for (int index = 0; index < length; index++)
            {
                result[index] = Math.Sqrt(result[index] / sum);
            }
            return result;
        }

        private static double Bessel(double value)
        {
            double sum = 1;
            double term = 1;
            for (int index = 1; index <= 64; index++)
            {
                term *= value * value / (4 * index * index);
                double next = sum + term;
                if (next == sum)
                {
                    return sum;
                }
                sum = next;
            }
            throw new InvalidOperationException("The AAC window series did not converge.");
        }
    }
}
