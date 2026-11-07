// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltPitchSearch
    {
        public const int HistorySamples = 2048;
        private const int MaximumLag = 720;
        private const int MinimumLag = 100;

        public static int Find(ReadOnlySpan<float> history, int channels, int stride)
        {
            Span<float> low = stackalloc float[HistorySamples / 2];
            low.Clear();
            for (int channel = 0; channel < channels; channel++)
            {
                ReadOnlySpan<float> source = history.Slice(channel * stride, HistorySamples);
                low[0] += .25f * source[1] + .5f * source[0];
                for (int at = 1; at < low.Length; at++)
                {
                    low[at] += .25f * source[2 * at - 1] + .25f * source[2 * at + 1] + .5f * source[2 * at];
                }
            }
            Whiten(low);
            int length = HistorySamples - MaximumLag, maximum = MaximumLag - MinimumLag;
            ReadOnlySpan<float> target = low[(MaximumLag / 2)..];
            Span<float> coarseTarget = stackalloc float[length / 4], coarseSource = stackalloc float[(length + maximum) / 4];
            for (int at = 0; at < coarseTarget.Length; at++) { coarseTarget[at] = target[at * 2]; }
            for (int at = 0; at < coarseSource.Length; at++) { coarseSource[at] = low[at * 2]; }
            Span<float> correlation = stackalloc float[maximum / 2];
            for (int lag = 0; lag < maximum / 4; lag++) { correlation[lag] = Dot(coarseTarget, coarseSource[lag..]); }
            (int best, int second) = Select(correlation[..(maximum / 4)], coarseSource, coarseTarget.Length);
            for (int lag = 0; lag < correlation.Length; lag++)
            {
                correlation[lag] = 0;
                if (Math.Abs(lag - 2 * best) <= 2 || Math.Abs(lag - 2 * second) <= 2)
                {
                    correlation[lag] = Math.Max(-1, Dot(target[..(length / 2)], low[lag..]));
                }
            }
            (best, _) = Select(correlation, low, length / 2);
            int offset = 0;
            if (best > 0 && best < correlation.Length - 1)
            {
                float before = correlation[best - 1], center = correlation[best], after = correlation[best + 1];
                if (after - before > .7f * (center - before)) { offset = 1; }
                else if (before - after > .7f * (center - after)) { offset = -1; }
            }
            return MaximumLag - 2 * best + offset;
        }

        private static void Whiten(Span<float> signal)
        {
            Span<float> correlation = stackalloc float[5], coefficients = stackalloc float[4], filter = stackalloc float[5];
            CeltLpcAnalysis.Correlate(signal, correlation, []);
            correlation[0] *= 1.0001f;
            for (int lag = 1; lag < correlation.Length; lag++) { correlation[lag] -= correlation[lag] * (.008f * lag) * (.008f * lag); }
            CeltLpcAnalysis.Predict(correlation, coefficients);
            float expansion = 1;
            for (int at = 0; at < coefficients.Length; at++) { expansion *= .9f; coefficients[at] *= expansion; }
            filter[0] = coefficients[0] + .8f;
            for (int at = 1; at < 4; at++) { filter[at] = coefficients[at] + .8f * coefficients[at - 1]; }
            filter[4] = .8f * coefficients[3];
            Span<float> memory = stackalloc float[5]; memory.Clear();
            for (int at = 0; at < signal.Length; at++)
            {
                float input = signal[at], value = input;
                for (int tap = 0; tap < filter.Length; tap++) { value += filter[tap] * memory[tap]; }
                for (int tap = 4; tap > 0; tap--) { memory[tap] = memory[tap - 1]; }
                memory[0] = input; signal[at] = value;
            }
        }

        private static (int Best, int Second) Select(ReadOnlySpan<float> correlation, ReadOnlySpan<float> signal, int length)
        {
            float energy = 1, firstValue = -1, secondValue = -1, firstEnergy = 0, secondEnergy = 0;
            int first = 0, second = 1;
            for (int at = 0; at < length; at++) { energy += signal[at] * signal[at]; }
            for (int lag = 0; lag < correlation.Length; lag++)
            {
                if (correlation[lag] > 0)
                {
                    float scaled = correlation[lag] * 1e-12f, value = scaled * scaled;
                    if (value * secondEnergy > secondValue * energy)
                    {
                        if (value * firstEnergy > firstValue * energy)
                        {
                            second = first; secondValue = firstValue; secondEnergy = firstEnergy;
                            first = lag; firstValue = value; firstEnergy = energy;
                        }
                        else { second = lag; secondValue = value; secondEnergy = energy; }
                    }
                }
                energy += signal[lag + length] * signal[lag + length] - signal[lag] * signal[lag];
                energy = Math.Max(1, energy);
            }
            return (first, second);
        }

        private static float Dot(ReadOnlySpan<float> first, ReadOnlySpan<float> second)
        {
            float sum = 0;
            for (int at = 0; at < first.Length; at++) { sum += first[at] * second[at]; }
            return sum;
        }
    }
}
