// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class SilkConcealment
    {
        private const int LpcHistory = 16;
        private const int PitchTaps = 5;
        private const int RandomMask = 127;

        public static void Update(SilkRecoveryState state, SilkFrameMode mode, SilkFrameIndices indices,
            SilkFilterParameters filters, SilkParameterBuffers parameters)
        {
            state.PreviousSignal = indices.Signal;
            int gain = 0;
            state.Ltp.Clear();
            if (indices.Signal == SilkSignalType.Voiced)
            {
                for (int distance = 0; distance < mode.Subframes && distance * mode.SamplesPerSubframe < parameters.PitchLags[mode.Subframes - 1]; distance++)
                {
                    int subframe = mode.Subframes - distance - 1;
                    ReadOnlySpan<short> taps = parameters.LtpQ14.Slice(subframe * PitchTaps, PitchTaps);
                    int candidate = 0;
                    foreach (short tap in taps) { candidate += tap; }
                    if (candidate > gain) { gain = candidate; state.PitchQ8 = parameters.PitchLags[subframe] << 8; }
                }
                int coefficient = gain;
                if (gain < 11469)
                {
                    int scale = (11469 << 10) / Math.Max(gain, 1);
                    coefficient = gain * (short)scale >> 10;
                }
                else if (gain > 15565)
                {
                    int scale = (15565 << 14) / gain;
                    coefficient = gain * (short)scale >> 14;
                }
                state.Ltp[2] = (short)coefficient;
            }
            else { state.PitchQ8 = 18 * mode.RateKhz << 8; }
            parameters.LpcQ12.Slice(mode.LpcOrder, mode.LpcOrder).CopyTo(state.Lpc);
            state.LtpScaleQ14 = filters.LtpScaleQ14;
            state.Gain1Q16 = parameters.GainsQ16[mode.Subframes - 2]; state.Gain2Q16 = parameters.GainsQ16[mode.Subframes - 1];
            state.SubframeSamples = mode.SamplesPerSubframe; state.Subframes = mode.Subframes;
            state.PreviousLag = parameters.PitchLags[mode.Subframes - 1];
        }

        public static void Synthesize(SilkRecoveryState state, SilkSynthesisState synthesis, SilkFrameMode mode,
            bool firstFrame, Span<short> output)
        {
            Span<short> lpc = state.Lpc[..mode.LpcOrder];
            if (firstFrame) { state.Lpc.Clear(); }
            int randomOffset = SelectNoise(synthesis.Excitation, state, mode);
            int harmonicGain = state.LossCount switch { 0 => 32440, _ => 31130 };
            int randomGain = state.PreviousSignal switch
            {
                SilkSignalType.Voiced => state.LossCount switch { 0 => 31130, _ => 26214 },
                SilkSignalType.Inactive or SilkSignalType.Unvoiced => state.LossCount switch { 0 => 32440, _ => 29491 },
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };
            SilkSignalMath.Expand(lpc, 64881);
            int randomScale = state.RandomScaleQ14;
            if (state.LossCount == 0)
            {
                randomScale = 16384;
                if (state.PreviousSignal == SilkSignalType.Voiced)
                {
                    foreach (short tap in state.Ltp) { randomScale -= tap; }
                    randomScale = Math.Max(3277, randomScale) * (short)state.LtpScaleQ14 >> 14;
                }
                else
                {
                    int inverse = Math.Clamp(SilkLpcStability.InverseGainQ30(lpc), 1 << 22, 1 << 27) << 3;
                    randomGain = (int)((long)inverse * (short)randomGain >> 16) >> 14;
                }
            }
            int lag = SilkFixedMath.RoundShift(state.PitchQ8, 8), history = synthesis.HistorySamples;
            int start = history - lag - mode.LpcOrder - PitchTaps / 2;
            Span<short> whitened = synthesis.Whitened;
            SilkLpcAnalysis.Apply(synthesis.OutputHistory.Slice(start, history - start), lpc, whitened[start..]);
            int inverseGain = Math.Min(SilkFixedMath.InversePositive(state.Gain2Q16, 46), int.MaxValue >> 1);
            Span<int> ltp = synthesis.LtpHistory;
            for (int at = start + mode.LpcOrder; at < history; at++) { ltp[at] = (int)((long)inverseGain * whitened[at] >> 16); }
            int position = history, seed = state.RandomSeed;
            for (int subframe = 0; subframe < mode.Subframes; subframe++)
            {
                for (int at = 0; at < mode.SamplesPerSubframe; at++)
                {
                    int prediction = 2;
                    for (int tap = 0; tap < PitchTaps; tap++) { prediction = unchecked(prediction + (int)((long)ltp[position - lag + 2 - tap] * state.Ltp[tap] >> 16)); }
                    seed = unchecked(seed * 196314165 + 907633515);
                    int excitation = synthesis.Excitation[randomOffset + ((seed >> 25) & RandomMask)];
                    ltp[position++] = unchecked((prediction + (int)((long)excitation * (short)randomScale >> 16)) << 2);
                }
                for (int tap = 0; tap < PitchTaps; tap++) { state.Ltp[tap] = (short)(harmonicGain * state.Ltp[tap] >> 15); }
                randomScale = randomScale * (short)randomGain >> 15;
                state.PitchQ8 = Math.Min(state.PitchQ8 + (int)((long)state.PitchQ8 * 655 >> 16), 18 * mode.RateKhz << 8);
                lag = SilkFixedMath.RoundShift(state.PitchQ8, 8);
            }
            Span<int> signal = ltp[(history - LpcHistory)..];
            synthesis.LpcHistory.CopyTo(signal);
            for (int at = 0; at < mode.FrameSamples; at++)
            {
                int prediction = mode.LpcOrder / 2;
                for (int tap = 0; tap < mode.LpcOrder; tap++) { prediction = unchecked(prediction + (int)((long)signal[LpcHistory + at - tap - 1] * lpc[tap] >> 16)); }
                int value = SilkFixedMath.Saturate32((long)signal[LpcHistory + at] + SilkFixedMath.SaturatingLeftShift(prediction, 4));
                signal[LpcHistory + at] = value;
                int scaled = unchecked((int)((long)value * (state.Gain2Q16 >> 6) >> 16));
                output[at] = (short)Math.Clamp(SilkFixedMath.RoundShift(scaled, 8), short.MinValue, short.MaxValue);
            }
            signal.Slice(mode.FrameSamples, LpcHistory).CopyTo(synthesis.LpcHistory);
            int retained = history - mode.FrameSamples;
            synthesis.OutputHistory.Slice(mode.FrameSamples, retained).CopyTo(synthesis.OutputHistory);
            output[..mode.FrameSamples].CopyTo(synthesis.OutputHistory[retained..]);
            state.RandomSeed = seed; state.RandomScaleQ14 = randomScale; state.PreviousLag = lag; state.LossCount++;
        }

        private static int SelectNoise(ReadOnlySpan<int> excitation, SilkRecoveryState state, SilkFrameMode mode)
        {
            Span<short> samples = stackalloc short[160];
            for (int half = 0; half < 2; half++)
            {
                int gain = half switch { 0 => state.Gain1Q16 >> 6, 1 => state.Gain2Q16 >> 6, _ => throw new InvalidOperationException() };
                for (int at = 0; at < mode.SamplesPerSubframe; at++)
                {
                    int value = unchecked((int)((long)excitation[(half + mode.Subframes - 2) * mode.SamplesPerSubframe + at] * gain >> 16));
                    samples[half * mode.SamplesPerSubframe + at] = (short)Math.Clamp(value >> 8, short.MinValue, short.MaxValue);
                }
            }
            (int first, int firstShift) = SilkSignalMath.Energy(samples[..mode.SamplesPerSubframe]);
            (int second, int secondShift) = SilkSignalMath.Energy(samples.Slice(mode.SamplesPerSubframe, mode.SamplesPerSubframe));
            int subframes = state.Subframes;
            if ((first >> secondShift) < (second >> firstShift)) { subframes--; }
            return Math.Max(0, subframes * state.SubframeSamples - RandomMask - 1);
        }

        public static void Connect(SilkRecoveryState state, Span<short> output)
        {
            if (state.LossCount > 0)
            {
                (state.LostEnergy, state.LostEnergyShift) = SilkSignalMath.Energy(output);
                state.PreviousLost = true; return;
            }
            if (state.PreviousLost)
            {
                (int energy, int shift) = SilkSignalMath.Energy(output);
                int previous = state.LostEnergy;
                if (shift > state.LostEnergyShift) { previous >>= shift - state.LostEnergyShift; }
                else { energy >>= state.LostEnergyShift - shift; }
                if (energy > previous)
                {
                    int headroom = BitOperations.LeadingZeroCount((uint)previous) - 1;
                    int fraction = (previous << headroom) / Math.Max(energy >> Math.Max(24 - headroom, 0), 1);
                    int gain = SilkSignalMath.SquareRoot(fraction) << 4, step = ((65536 - gain) / output.Length) << 2;
                    for (int at = 0; at < output.Length; at++)
                    {
                        output[at] = (short)((long)gain * output[at] >> 16); gain += step;
                        if (gain > 65536) { break; }
                    }
                }
            }
            state.PreviousLost = false;
        }
    }
}
