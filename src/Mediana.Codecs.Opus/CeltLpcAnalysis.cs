// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltLpcAnalysis
    {
        public const int Order = 24;

        public static void Correlate(ReadOnlySpan<float> signal, Span<float> correlation, ReadOnlySpan<float> window)
        {
            int order = correlation.Length - 1, length = signal.Length, fast = length - order;
            ArgumentOutOfRangeException.ThrowIfLessThan(fast, 1);
            Span<float> tapered = stackalloc float[length];
            scoped ReadOnlySpan<float> input = signal;
            if (!window.IsEmpty)
            {
                signal.CopyTo(tapered);
                for (int at = 0; at < window.Length; at++)
                {
                    tapered[at] = signal[at] * window[at];
                    tapered[length - at - 1] = signal[length - at - 1] * window[at];
                }
                input = tapered;
            }
            for (int lag = 0; lag <= order; lag++)
            {
                float sum = 0, tail = 0;
                for (int at = 0; at < fast; at++) { sum += input[at] * input[at + lag]; }
                for (int at = fast + lag; at < length; at++) { tail += input[at] * input[at - lag]; }
                correlation[lag] = sum + tail;
            }
        }

        public static void Predict(ReadOnlySpan<float> correlation, Span<float> coefficients)
        {
            float error = correlation[0];
            coefficients.Clear();
            if (error <= 1e-10f) { return; }
            for (int order = 0; order < coefficients.Length; order++)
            {
                float sum = 0;
                for (int at = 0; at < order; at++) { sum += coefficients[at] * correlation[order - at]; }
                sum += correlation[order + 1];
                float reflection = -sum / error;
                coefficients[order] = reflection;
                for (int at = 0; at < (order + 1) / 2; at++)
                {
                    float first = coefficients[at], last = coefficients[order - at - 1];
                    coefficients[at] = first + reflection * last;
                    coefficients[order - at - 1] = last + reflection * first;
                }
                error -= reflection * reflection * error;
                if (error <= .001f * correlation[0]) { break; }
            }
        }

        public static void Residual(ReadOnlySpan<float> signal, ReadOnlySpan<float> coefficients, Span<float> output)
        {
            int order = coefficients.Length;
            ArgumentOutOfRangeException.ThrowIfLessThan(signal.Length, order + output.Length, nameof(signal));
            for (int at = 0; at < output.Length; at++)
            {
                float value = signal[order + at];
                for (int tap = 0; tap < order; tap++) { value += coefficients[order - tap - 1] * signal[at + tap]; }
                output[at] = value;
            }
        }

        public static void Synthesize(Span<float> signal, ReadOnlySpan<float> coefficients, int samples)
        {
            int order = coefficients.Length;
            ArgumentOutOfRangeException.ThrowIfLessThan(signal.Length, order + samples, nameof(signal));
            Span<float> negative = stackalloc float[order + samples];
            for (int at = 0; at < order; at++) { negative[at] = -signal[at]; }
            negative[order..].Clear();
            Span<float> values = stackalloc float[4];
            for (int at = 0; at < samples; at += 4)
            {
                int count = Math.Min(4, samples - at);
                for (int lane = 0; lane < count; lane++)
                {
                    float value = signal[order + at + lane];
                    for (int tap = 0; tap < order; tap++) { value += coefficients[order - tap - 1] * negative[at + lane + tap]; }
                    values[lane] = value;
                }
                for (int lane = 0; lane < count; lane++)
                {
                    float value = values[lane];
                    for (int prior = lane - 1; prior >= 0; prior--) { value += negative[order + at + prior] * coefficients[lane - prior - 1]; }
                    signal[order + at + lane] = value; negative[order + at + lane] = -value;
                }
            }
        }
    }
}
