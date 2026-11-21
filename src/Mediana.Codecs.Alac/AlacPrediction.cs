// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Alac
{
    internal static class AlacPrediction
    {
        public static void Restore(Span<int> samples, Span<short> coefficients, int precision, int shift, CancellationToken token)
        {
            int order = coefficients.Length;
            if (order == 0)
            {
                return;
            }
            if (order == 31)
            {
                for (int i = 1; i < samples.Length; i++)
                {
                    samples[i] = Wrap(unchecked(samples[i] + samples[i - 1]), precision);
                }
                return;
            }
            if (order >= samples.Length || shift == 0)
            {
                throw new InvalidDataException("The ALAC predictor order or quantization is invalid.");
            }
            for (int i = 1; i <= order; i++)
            {
                samples[i] = Wrap(unchecked(samples[i] + samples[i - 1]), precision);
            }
            for (int i = order + 1; i < samples.Length; i++)
            {
                if ((i & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int baseline = samples[i - order - 1];
                int sum = 1 << (shift - 1);
                for (int j = 0; j < order; j++)
                {
                    sum = unchecked(sum + (samples[i - j - 1] - baseline) * coefficients[j]);
                }
                int error = samples[i];
                samples[i] = Wrap(unchecked(error + baseline + (sum >> shift)), precision);
                int direction = Math.Sign(error);
                for (int j = order - 1; j >= 0 && Math.Sign(error) == direction && direction != 0; j--)
                {
                    int difference = unchecked(baseline - samples[i - j - 1]);
                    int adjustment = Math.Sign(difference) * direction;
                    coefficients[j] = unchecked((short)(coefficients[j] - adjustment));
                    error = unchecked(error - (order - j) * ((adjustment * difference) >> shift));
                }
            }
        }

        private static int Wrap(int value, int precision) => (value << (32 - precision)) >> (32 - precision);
    }
}
