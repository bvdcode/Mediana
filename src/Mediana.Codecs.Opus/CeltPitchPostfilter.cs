// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltPitchPostfilter
    {
        public const int MinimumPeriod = 15;
        public const int MaximumPeriod = 1024;
        public const int HistorySamples = MaximumPeriod + 2;

        public static void Apply(Span<float> signal, int start, int count, CeltPostFilterParameters previous,
            CeltPostFilterParameters current, ReadOnlySpan<float> window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(start, HistorySamples);
            ArgumentOutOfRangeException.ThrowIfLessThan(count, CeltTransformPlan.Overlap);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(start, signal.Length - count);
            ArgumentOutOfRangeException.ThrowIfLessThan(window.Length, CeltTransformPlan.Overlap, nameof(window));
            Validate(previous); Validate(current);
            if (signal.Overlaps(window)) { throw new ArgumentException("The pitch-filter window must not overlap the signal.", nameof(window)); }
            if (previous.Gain == 0 && current.Gain == 0) { return; }
            int oldPeriod = Math.Max(MinimumPeriod, previous.Period), period = Math.Max(MinimumPeriod, current.Period);
            (float oldCenter, float oldNear, float oldFar) = Taps(previous);
            (float center, float near, float far) = Taps(current);
            int transition = CeltTransformPlan.Overlap;
            if (oldPeriod == period && previous.Gain == current.Gain && previous.Tapset == current.Tapset) { transition = 0; }
            float x1 = signal[start - period + 1], x2 = signal[start - period];
            float x3 = signal[start - period - 1], x4 = signal[start - period - 2];
            for (int at = 0; at < transition; at++)
            {
                int index = start + at;
                float x0 = signal[index - period + 2], blend = window[at] * window[at], oldBlend = 1 - blend;
                signal[index] = signal[index]
                    + oldBlend * oldCenter * signal[index - oldPeriod]
                    + oldBlend * oldNear * (signal[index - oldPeriod + 1] + signal[index - oldPeriod - 1])
                    + oldBlend * oldFar * (signal[index - oldPeriod + 2] + signal[index - oldPeriod - 2])
                    + blend * center * x2 + blend * near * (x1 + x3) + blend * far * (x0 + x4);
                x4 = x3; x3 = x2; x2 = x1; x1 = x0;
            }
            if (current.Gain == 0 || transition == count) { return; }
            int first = start + transition;
            x1 = signal[first - period + 1]; x2 = signal[first - period];
            x3 = signal[first - period - 1]; x4 = signal[first - period - 2];
            for (int at = first; at < start + count; at++)
            {
                float x0 = signal[at - period + 2];
                signal[at] = signal[at] + center * x2 + near * (x1 + x3) + far * (x0 + x4);
                x4 = x3; x3 = x2; x2 = x1; x1 = x0;
            }
        }

        private static (float Center, float Near, float Far) Taps(CeltPostFilterParameters parameters)
        {
            (float center, float near, float far) = parameters.Tapset switch
            {
                0 => (.3066406250f, .2170410156f, .1296386719f),
                1 => (.4638671875f, .2680664062f, 0f),
                2 => (.7998046875f, .1000976562f, 0f),
                _ => throw new ArgumentOutOfRangeException(nameof(parameters))
            };
            return (parameters.Gain * center, parameters.Gain * near, parameters.Gain * far);
        }

        private static void Validate(CeltPostFilterParameters parameters)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(parameters.Period, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Period, MaximumPeriod, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfNegative(parameters.Tapset, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Tapset, 2, nameof(parameters));
            if (!float.IsFinite(parameters.Gain) || parameters.Gain < 0 || parameters.Gain > .75f)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "The CELT pitch gain must be between zero and 0.75.");
            }
        }
    }
}
