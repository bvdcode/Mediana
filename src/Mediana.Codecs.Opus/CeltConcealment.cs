// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltConcealment
    {
        private const int ExcitationSamples = CeltPitchPostfilter.MaximumPeriod;
        private const int History = CeltPitchSearch.HistorySamples;

        public static void Periodic(CeltRecoveryState state, Span<float> signal, int channels, int stride,
            int samples, ReadOnlySpan<float> window)
        {
            if (!state.PreviousPeriodic) { state.Pitch = CeltPitchSearch.Find(signal, channels, stride); }
            int pitch = state.Pitch, excitationLength = Math.Min(2 * pitch, ExcitationSamples);
            int offset = ExcitationSamples - pitch, length = samples + CeltTransformPlan.Overlap;
            float fade = 1;
            if (state.PreviousPeriodic) { fade = .8f; }
            Span<float> excitation = stackalloc float[ExcitationSamples + CeltLpcAnalysis.Order];
            Span<float> residual = stackalloc float[ExcitationSamples];
            Span<float> correlation = stackalloc float[CeltLpcAnalysis.Order + 1];
            for (int channel = 0; channel < channels; channel++)
            {
                Span<float> plane = signal.Slice(channel * stride, stride);
                Span<float> coefficients = state.Coefficients.Slice(channel * CeltLpcAnalysis.Order, CeltLpcAnalysis.Order);
                plane.Slice(History - excitation.Length, excitation.Length).CopyTo(excitation);
                if (!state.PreviousPeriodic)
                {
                    CeltLpcAnalysis.Correlate(excitation[CeltLpcAnalysis.Order..], correlation, window);
                    correlation[0] *= 1.0001f;
                    for (int lag = 1; lag < correlation.Length; lag++) { correlation[lag] -= correlation[lag] * (.008f * .008f) * lag * lag; }
                    CeltLpcAnalysis.Predict(correlation, coefficients);
                }
                CeltLpcAnalysis.Residual(excitation[(ExcitationSamples - excitationLength)..], coefficients, residual[..excitationLength]);
                residual[..excitationLength].CopyTo(excitation[(CeltLpcAnalysis.Order + ExcitationSamples - excitationLength)..]);
                float recent = 1, earlier = 1;
                for (int at = 0; at < excitationLength / 2; at++)
                {
                    float value = excitation[CeltLpcAnalysis.Order + ExcitationSamples - excitationLength / 2 + at];
                    recent += value * value;
                    value = excitation[CeltLpcAnalysis.Order + ExcitationSamples - excitationLength + at];
                    earlier += value * value;
                }
                float decay = MathF.Sqrt(Math.Min(recent, earlier) / earlier), attenuation = fade * decay;
                float expectedEnergy = 0;
                for (int at = 0, period = 0; at < length; at++, period++)
                {
                    if (period >= pitch) { period -= pitch; attenuation *= decay; }
                    plane[History + at] = attenuation * excitation[CeltLpcAnalysis.Order + offset + period];
                    float previous = plane[History - ExcitationSamples + offset + period];
                    expectedEnergy += previous * previous;
                }
                Span<float> synthesis = plane.Slice(History - CeltLpcAnalysis.Order, CeltLpcAnalysis.Order + length);
                CeltLpcAnalysis.Synthesize(synthesis, coefficients, length);
                float energy = 0;
                foreach (float value in plane.Slice(History, length)) { energy += value * value; }
                if (!(expectedEnergy > .2f * energy)) { plane.Slice(History, length).Clear(); }
                else if (expectedEnergy < energy)
                {
                    float ratio = MathF.Sqrt((expectedEnergy + 1) / (energy + 1));
                    for (int at = 0; at < window.Length; at++) { plane[History + at] *= 1 - window[at] * (1 - ratio); }
                    for (int at = window.Length; at < length; at++) { plane[History + at] *= ratio; }
                }
            }
            state.PreviousPeriodic = true; state.FoldOverlap = true;
        }

        public static void Noise(CeltRecoveryState state, CeltBandMode mode, Span<float> energies,
            Span<float> coefficients, ref uint seed)
        {
            int samples = CeltBandTables.ShortTransformSize << mode.FrameShift;
            float decay = .5f;
            if (state.LostDuration == 0) { decay = 1.5f; }
            coefficients[..(samples * mode.Channels)].Clear();
            for (int channel = 0; channel < mode.Channels; channel++)
            {
                for (int band = mode.FirstBand; band < mode.EndBand; band++)
                {
                    int index = channel * CeltBandTables.BandCount + band;
                    energies[index] = Math.Max(state.Background[index], energies[index] - decay);
                    int first = CeltBandTables.Boundaries[band] << mode.FrameShift;
                    int end = CeltBandTables.Boundaries[band + 1] << mode.FrameShift;
                    Span<float> vector = coefficients.Slice(channel * samples + first, end - first);
                    for (int at = 0; at < vector.Length; at++)
                    {
                        seed = unchecked(seed * 1664525 + 1013904223);
                        vector[at] = ((int)seed >> 20) * (1f / 16384);
                    }
                    CeltVectorMath.Normalize(vector, 1);
                }
            }
            state.PreviousPeriodic = false; state.SkipPitch = true;
        }

        public static void Fold(Span<float> signal, int channels, int stride, CeltPostFilterParameters filter, ReadOnlySpan<float> window)
        {
            Span<float> filtered = stackalloc float[CeltTransformPlan.Overlap];
            for (int channel = 0; channel < channels; channel++)
            {
                ReadOnlySpan<float> plane = signal.Slice(channel * stride, stride);
                CeltPitchPostfilter.Remove(plane, History, filtered, filter);
                for (int at = 0; at < CeltTransformPlan.Overlap / 2; at++)
                {
                    signal[channel * stride + History + at] = window[at] * filtered[window.Length - at - 1]
                        + window[window.Length - at - 1] * filtered[at];
                }
            }
        }
    }
}
