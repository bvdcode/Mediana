// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkComfortNoise
    {
        private const int HistoryLength = 16;

        public static void Update(SilkRecoveryState state, SilkSynthesisState synthesis, SilkFrameMode mode,
            SilkSignalType signal, SilkParameterBuffers parameters)
        {
            if (signal == SilkSignalType.Inactive)
            {
                for (int at = 0; at < mode.LpcOrder; at++)
                {
                    int delta = synthesis.PreviousLsf[at] - state.NoiseLsf[at];
                    state.NoiseLsf[at] += (short)(delta * 16348 >> 16);
                }
                int loudest = 0;
                for (int at = 1; at < mode.Subframes; at++)
                {
                    if (parameters.GainsQ16[at] > parameters.GainsQ16[loudest]) { loudest = at; }
                }
                int samples = mode.SamplesPerSubframe;
                state.NoiseExcitation[..(mode.FrameSamples - samples)].CopyTo(state.NoiseExcitation[samples..]);
                synthesis.Excitation.Slice(loudest * samples, samples).CopyTo(state.NoiseExcitation);
                for (int at = 0; at < mode.Subframes; at++)
                {
                    int gain = parameters.GainsQ16[at];
                    state.NoiseGainQ16 += (int)((long)(gain - state.NoiseGainQ16) * 4634 >> 16);
                    if ((int)((long)state.NoiseGainQ16 * 46396 >> 16) > gain) { state.NoiseGainQ16 = gain; }
                }
            }
            state.NoiseHistory[..mode.LpcOrder].Clear();
        }

        public static void Synthesize(SilkRecoveryState state, SilkFrameMode mode, Span<short> output)
        {
            int gain = (int)((long)state.RandomScaleQ14 * state.Gain2Q16 >> 16);
            int energy, gainQ16;
            if (gain >= 1 << 21 || state.NoiseGainQ16 > 1 << 23)
            {
                energy = unchecked((state.NoiseGainQ16 >> 16) * (state.NoiseGainQ16 >> 16) - ((gain >> 16) * (gain >> 16) << 5));
                gainQ16 = SilkSignalMath.SquareRoot(energy) << 16;
            }
            else
            {
                energy = unchecked((int)((long)state.NoiseGainQ16 * state.NoiseGainQ16 >> 16) - ((int)((long)gain * gain >> 16) << 5));
                gainQ16 = SilkSignalMath.SquareRoot(energy) << 8;
            }
            int scale = gainQ16 >> 6, mask = 255, seed = state.NoiseSeed;
            while (mask > mode.FrameSamples) { mask >>= 1; }
            Span<int> working = stackalloc int[SilkFrameMode.MaximumFrameSamples + HistoryLength];
            Span<short> lpc = stackalloc short[16];
            SilkLpcConverter.Convert(state.NoiseLsf[..mode.LpcOrder], lpc);
            state.NoiseHistory.CopyTo(working);
            for (int at = 0; at < mode.FrameSamples; at++)
            {
                seed = unchecked(seed * 196314165 + 907633515);
                int excitation = state.NoiseExcitation[(seed >> 24) & mask];
                int prediction = mode.LpcOrder / 2;
                for (int tap = 0; tap < mode.LpcOrder; tap++)
                {
                    prediction = unchecked(prediction + (int)((long)working[HistoryLength + at - tap - 1] * lpc[tap] >> 16));
                }
                int value = SilkFixedMath.Saturate32((long)excitation + SilkFixedMath.SaturatingLeftShift(prediction, 4));
                working[HistoryLength + at] = value;
                int scaled = unchecked((int)((long)value * scale >> 16));
                int noise = Math.Clamp(SilkFixedMath.RoundShift(scaled, 8), short.MinValue, short.MaxValue);
                output[at] = (short)Math.Clamp(output[at] + noise, short.MinValue, short.MaxValue);
            }
            working.Slice(mode.FrameSamples, HistoryLength).CopyTo(state.NoiseHistory);
            state.NoiseSeed = seed;
        }
    }
}
