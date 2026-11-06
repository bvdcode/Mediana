// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkStereoSynthesis
    {
        private const int InterpolationMilliseconds = 8;

        public static void Unmix(SilkStereoState state, SilkFrameMode mode, SilkStereoPrediction prediction,
            Span<short> mid, Span<short> side)
        {
            ArgumentNullException.ThrowIfNull(state);
            int samples = mode.FrameSamples, interpolation = InterpolationMilliseconds * mode.RateKhz;
            ArgumentOutOfRangeException.ThrowIfLessThan(mid.Length, samples, nameof(mid));
            ArgumentOutOfRangeException.ThrowIfLessThan(side.Length, samples, nameof(side));
            if (mid[..samples].Overlaps(side[..samples])) { throw new ArgumentException("SILK mid and side signals must not overlap."); }
            int denominator = 65536 / interpolation;
            int low = state.Prediction.LowQ13, high = state.Prediction.HighQ13;
            int lowStep = SilkFixedMath.RoundShift(unchecked((short)(prediction.LowQ13 - low)) * denominator, 16);
            int highStep = SilkFixedMath.RoundShift(unchecked((short)(prediction.HighQ13 - high)) * denominator, 16);
            short mid2 = state.MidPrevious2, mid1 = state.MidPrevious1;
            short side2 = state.SidePrevious2, side1 = state.SidePrevious1;
            for (int at = 0; at < samples; at++)
            {
                if (at < interpolation) { low += lowStep; high += highStep; }
                else { low = prediction.LowQ13; high = prediction.HighQ13; }
                short currentMid = mid[at], currentSide = side[at];
                int lowPassed = (mid2 + currentMid + 2 * mid1) << 9;
                int restoredSide = (side1 << 8) + (int)((long)lowPassed * unchecked((short)low) >> 16);
                restoredSide += (int)((long)(mid1 << 11) * unchecked((short)high) >> 16);
                int roundedSide = Math.Clamp(SilkFixedMath.RoundShift(restoredSide, 8), short.MinValue, short.MaxValue);
                mid[at] = (short)Math.Clamp(mid1 + roundedSide, short.MinValue, short.MaxValue);
                side[at] = (short)Math.Clamp(mid1 - roundedSide, short.MinValue, short.MaxValue);
                mid2 = mid1; mid1 = currentMid; side2 = side1; side1 = currentSide;
            }
            state.MidPrevious2 = mid2; state.MidPrevious1 = mid1;
            state.SidePrevious2 = side2; state.SidePrevious1 = side1;
            state.Prediction = prediction;
        }

        public static void DelayMono(SilkStereoState state, Span<short> samples)
        {
            ArgumentNullException.ThrowIfNull(state);
            short previous2 = state.MidPrevious2, previous1 = state.MidPrevious1;
            for (int at = 0; at < samples.Length; at++)
            {
                short current = samples[at];
                samples[at] = previous1; previous2 = previous1; previous1 = current;
            }
            state.MidPrevious2 = previous2; state.MidPrevious1 = previous1;
        }
    }
}
