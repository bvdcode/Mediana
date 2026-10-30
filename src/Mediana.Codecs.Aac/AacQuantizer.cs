// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacQuantizer
    {
        public const int MaximumMagnitude = 8191;
        public const int MaximumScale = 255;
        private static readonly double[] Magnitudes = CreateMagnitudes();
        private static readonly double[] InverseGains = CreateGains(0.25);
        private static readonly double[] ForwardGains = CreateGains(-3.0 / 16);

        public static double Magnitude(int value) => Magnitudes[value];
        public static double Gain(int scale) => InverseGains[scale];
        public static double Reconstruct(int magnitude, int scale) => Magnitude(magnitude) * Gain(scale);

        public static double Quantize(ReadOnlySpan<double> spectrum, ReadOnlySpan<double> powered, Span<int> output, int scale)
        {
            double forward = ForwardGains[scale];
            double inverse = InverseGains[scale];
            double error = 0;
            for (int bin = 0; bin < spectrum.Length; bin++)
            {
                double value = Math.Abs(spectrum[bin]);
                double position = powered[bin] * forward;
                if (position >= MaximumMagnitude + 1)
                {
                    return double.PositiveInfinity;
                }
                int magnitude = (int)position;
                if (magnitude < MaximumMagnitude && value > (Magnitudes[magnitude] + Magnitudes[magnitude + 1]) * inverse * 0.5)
                {
                    magnitude++;
                }
                output[bin] = spectrum[bin] < 0 ? -magnitude : magnitude;
                double difference = value - Magnitudes[magnitude] * inverse;
                error += difference * difference;
            }
            return error;
        }

        private static double[] CreateMagnitudes()
        {
            double[] values = new double[MaximumMagnitude + 1];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = Math.Pow(index, 4.0 / 3);
            }
            return values;
        }

        private static double[] CreateGains(double exponent)
        {
            double[] values = new double[MaximumScale + 1];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = Math.Pow(2, exponent * (index - 100));
            }
            return values;
        }
    }
}
