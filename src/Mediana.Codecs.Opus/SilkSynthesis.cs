// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkSynthesis
    {
        private const int LpcHistoryLength = 16;
        private const int PitchTaps = 5;

        public static void Process(SilkSynthesisState state, SilkFrameMode mode, SilkFrameIndices indices,
            SilkFilterParameters filters, SilkParameterBuffers parameters, ReadOnlySpan<short> pulses, Span<short> output)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.SampleRate != mode.SampleRate) { throw new ArgumentException("The SILK synthesis state has a different sample rate.", nameof(state)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(pulses.Length, mode.FrameSamples, nameof(pulses));
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, mode.FrameSamples, nameof(output));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.GainsQ16.Length, mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.LpcQ12.Length, 2 * mode.LpcOrder, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.PitchLags.Length, mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.LtpQ14.Length, PitchTaps * mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(state.PreviousGainQ16, nameof(state));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(filters.InterpolationQ2, 4, nameof(filters));
            bool voiced = indices.Signal switch
            {
                SilkSignalType.Inactive or SilkSignalType.Unvoiced => false,
                SilkSignalType.Voiced => true,
                _ => throw new ArgumentOutOfRangeException(nameof(indices))
            };
            foreach (int gain in parameters.GainsQ16[..mode.Subframes])
            {
                if (gain is < 81920 or > 1686110208) { throw new ArgumentOutOfRangeException(nameof(parameters)); }
            }
            if (voiced)
            {
                foreach (int lag in parameters.PitchLags[..mode.Subframes])
                {
                    if (lag < 2 * mode.RateKhz || lag > 18 * mode.RateKhz) { throw new ArgumentOutOfRangeException(nameof(parameters)); }
                }
                if (filters.LtpScaleQ14 is not (15565 or 12288 or 8192)) { throw new ArgumentOutOfRangeException(nameof(filters)); }
            }
            if (pulses.Overlaps(output) || state.OutputHistory.Overlaps(output))
            {
                throw new ArgumentException("SILK synthesis output must not overlap the pulses or output history.");
            }
            Span<int> excitation = state.Excitation[..mode.FrameSamples], working = state.LpcWorking, ltp = state.LtpHistory;
            SilkExcitation.Reconstruct(pulses[..mode.FrameSamples], excitation, indices.Signal, indices.QuantizationOffset, indices.Seed);
            state.LpcHistory.CopyTo(working);
            int ltpAt = state.HistorySamples, subframeSamples = mode.SamplesPerSubframe;
            for (int subframe = 0; subframe < mode.Subframes; subframe++)
            {
                ReadOnlySpan<short> lpc = parameters.LpcQ12.Slice((subframe / 2) * mode.LpcOrder, mode.LpcOrder);
                int gain = parameters.GainsQ16[subframe], adjustment = 65536, inverse = SilkFixedMath.InversePositive(gain, 47);
                if (gain != state.PreviousGainQ16)
                {
                    adjustment = SilkFixedMath.DividePositive(state.PreviousGainQ16, gain, 16);
                    for (int at = 0; at < LpcHistoryLength; at++) { working[at] = (int)((long)adjustment * working[at] >> 16); }
                }
                state.PreviousGainQ16 = gain;
                Span<int> residual = excitation.Slice(subframe * subframeSamples, subframeSamples);
                if (voiced)
                {
                    int lag = parameters.PitchLags[subframe];
                    if (subframe == 0 || (subframe == 2 && filters.InterpolationQ2 < 4))
                    {
                        Rewhiten(state, mode, lpc, output, subframe, lag, inverse, filters.LtpScaleQ14, ltpAt);
                    }
                    else if (adjustment != 65536)
                    {
                        for (int at = ltpAt - lag - 2; at < ltpAt; at++) { ltp[at] = (int)((long)adjustment * ltp[at] >> 16); }
                    }
                    ReadOnlySpan<short> taps = parameters.LtpQ14.Slice(subframe * PitchTaps, PitchTaps);
                    Span<int> predicted = state.Residual;
                    for (int at = 0; at < subframeSamples; at++)
                    {
                        int prediction = 2;
                        for (int tap = 0; tap < PitchTaps; tap++)
                        {
                            prediction = unchecked(prediction + (int)((long)ltp[ltpAt - lag + 2 - tap] * taps[tap] >> 16));
                        }
                        predicted[at] = unchecked(residual[at] + (prediction << 1));
                        ltp[ltpAt++] = unchecked(predicted[at] << 1);
                    }
                    residual = predicted;
                }
                for (int at = 0; at < subframeSamples; at++)
                {
                    int prediction = mode.LpcOrder / 2;
                    for (int tap = 0; tap < mode.LpcOrder; tap++)
                    {
                        prediction = unchecked(prediction + (int)((long)working[LpcHistoryLength + at - tap - 1] * lpc[tap] >> 16));
                    }
                    int value = SilkFixedMath.Saturate32((long)residual[at] + SilkFixedMath.SaturatingLeftShift(prediction, 4));
                    working[LpcHistoryLength + at] = value;
                    int scaled = unchecked((int)((long)value * (gain >> 6) >> 16));
                    output[subframe * subframeSamples + at] = (short)Math.Clamp(SilkFixedMath.RoundShift(scaled, 8), short.MinValue, short.MaxValue);
                }
                working.Slice(subframeSamples, LpcHistoryLength).CopyTo(working);
            }
            working[..LpcHistoryLength].CopyTo(state.LpcHistory);
            int retained = state.HistorySamples - mode.FrameSamples;
            state.OutputHistory.Slice(mode.FrameSamples, retained).CopyTo(state.OutputHistory);
            output[..mode.FrameSamples].CopyTo(state.OutputHistory[retained..]);
        }

        private static void Rewhiten(SilkSynthesisState state, SilkFrameMode mode, ReadOnlySpan<short> lpc,
            ReadOnlySpan<short> output, int subframe, int lag, int inverse, int scale, int ltpAt)
        {
            int start = state.HistorySamples - lag - mode.LpcOrder - 2;
            if (subframe == 2) { output[..(2 * mode.SamplesPerSubframe)].CopyTo(state.OutputHistory[state.HistorySamples..]); }
            ReadOnlySpan<short> input = state.OutputHistory.Slice(start + subframe * mode.SamplesPerSubframe, state.HistorySamples - start);
            Span<short> whitened = state.Whitened[start..];
            SilkLpcAnalysis.Apply(input, lpc, whitened);
            if (subframe == 0) { inverse = (int)((long)inverse * (short)scale >> 16) << 2; }
            Span<int> ltp = state.LtpHistory;
            ReadOnlySpan<short> history = state.Whitened;
            for (int at = 0; at < lag + 2; at++)
            {
                ltp[ltpAt - at - 1] = (int)((long)inverse * history[state.HistorySamples - at - 1] >> 16);
            }
        }
    }
}
