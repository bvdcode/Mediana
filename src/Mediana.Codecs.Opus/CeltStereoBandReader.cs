// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltStereoBandReader
    {
        public static uint Read(ref OpusRangeDecoder decoder, ref CeltBandDecoderState state, scoped Span<float> left,
            scoped Span<float> right, int budgetEighths, CeltBandResolution resolution, scoped ReadOnlySpan<float> lowBand,
            scoped Span<float> lowBandOutput, uint fill)
        {
            CeltBandReader.Validate(state, left, resolution, lowBand, lowBandOutput, 1);
            if (right.Length != left.Length || left.Overlaps(right))
            {
                throw new ArgumentException("CELT stereo bands require separate equally sized channel buffers.", nameof(right));
            }
            if (left.Length == 1)
            {
                CeltBandReader.ReadSign(ref decoder, ref state, left);
                CeltBandReader.ReadSign(ref decoder, ref state, right);
                if (!lowBandOutput.IsEmpty) { lowBandOutput[0] = left[0]; }
                return 1;
            }
            CeltSplitMode mode = new(state.Band, resolution.FrameShift, left.Length, resolution.Blocks, resolution.Blocks,
                true, state.IntensityBand, state.DisableInversion);
            CeltSplitAngle split = CeltSplitAngleReader.Read(ref decoder, mode, budgetEighths, state.RemainingEighths, fill);
            int budget = budgetEighths - split.EighthBitsUsed;
            float midGain = split.MidGain * (1f / 32768), sideGain = split.SideGain * (1f / 32768);
            state.RemainingEighths -= split.EighthBitsUsed;
            uint mask;
            if (left.Length == 2)
            {
                int sideBits = 0;
                if (split.Angle is > 0 and < 16384) { sideBits = 8; }
                state.RemainingEighths -= sideBits;
                int sign = 1;
                if (sideBits != 0 && decoder.ReadRawBits(1) != 0) { sign = -1; }
                scoped Span<float> dominant = left, other = right;
                if (split.Angle > 8192) { dominant = right; other = left; }
                mask = CeltBandReader.Read(ref decoder, ref state, dominant, budget - sideBits, resolution,
                    lowBand, lowBandOutput, 1, fill);
                other[0] = -sign * dominant[1];
                other[1] = sign * dominant[0];
                for (int at = 0; at < 2; at++)
                {
                    float mid = midGain * left[at], side = sideGain * right[at];
                    left[at] = mid - side;
                    right[at] = mid + side;
                }
            }
            else
            {
                int midBudget = Math.Max(0, Math.Min(budget, (budget - split.AllocationDelta) / 2));
                int sideBudget = budget - midBudget;
                int before = state.RemainingEighths;
                if (midBudget >= sideBudget)
                {
                    mask = CeltBandReader.Read(ref decoder, ref state, left, midBudget, resolution, lowBand, lowBandOutput, 1, split.Fill);
                    int unused = midBudget - (before - state.RemainingEighths);
                    if (unused > 24 && split.Angle != 0) { sideBudget += unused - 24; }
                    mask |= CeltBandReader.Read(ref decoder, ref state, right, sideBudget, resolution, default, default,
                        sideGain, split.Fill >> resolution.Blocks);
                }
                else
                {
                    mask = CeltBandReader.Read(ref decoder, ref state, right, sideBudget, resolution, default, default,
                        sideGain, split.Fill >> resolution.Blocks);
                    int unused = sideBudget - (before - state.RemainingEighths);
                    if (unused > 24 && split.Angle != 16384) { midBudget += unused - 24; }
                    mask |= CeltBandReader.Read(ref decoder, ref state, left, midBudget, resolution, lowBand, lowBandOutput, 1, split.Fill);
                }
                Merge(left, right, midGain);
            }
            if (split.Inverted)
            {
                for (int at = 0; at < right.Length; at++) { right[at] = -right[at]; }
            }
            return mask;
        }

        private static void Merge(Span<float> left, Span<float> right, float midGain)
        {
            float correlation = 0, sideEnergy = 0;
            for (int at = 0; at < left.Length; at++)
            {
                correlation += right[at] * left[at];
                sideEnergy += right[at] * right[at];
            }
            correlation *= midGain;
            float leftEnergy = midGain * midGain + sideEnergy - 2 * correlation;
            float rightEnergy = midGain * midGain + sideEnergy + 2 * correlation;
            if (leftEnergy < .0006f || rightEnergy < .0006f)
            {
                left.CopyTo(right);
                return;
            }
            float leftScale = 1f / MathF.Sqrt(leftEnergy), rightScale = 1f / MathF.Sqrt(rightEnergy);
            for (int at = 0; at < left.Length; at++)
            {
                float mid = midGain * left[at], side = right[at];
                left[at] = leftScale * (mid - side);
                right[at] = rightScale * (mid + side);
            }
        }
    }
}
