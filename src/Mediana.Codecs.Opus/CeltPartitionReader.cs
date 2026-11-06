// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class CeltPartitionReader
    {
        public static uint Read(ref OpusRangeDecoder decoder, ref CeltBandDecoderState state, scoped Span<float> vector,
            int budgetEighths, int blocks, scoped ReadOnlySpan<float> lowBand, int frameShift, float gain, uint fill)
        {
            ArgumentNullException.ThrowIfNull(state.Codebook);
            ArgumentOutOfRangeException.ThrowIfNegative(state.Band);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state.Band, CeltBandTables.BandCount);
            ArgumentOutOfRangeException.ThrowIfLessThan(frameShift, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            int dimensions = (CeltBandTables.Width(state.Band) << (frameShift + 1)) / 2;
            if (vector.Length < 2 || vector.Length != dimensions)
            {
                throw new ArgumentException("The CELT partition dimensions do not match its band and frame size.", nameof(vector));
            }
            if (blocks is < 1 or > CeltBandTables.MaximumTimeBlocks || !BitOperations.IsPow2(blocks) || vector.Length % blocks != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blocks));
            }
            if (!lowBand.IsEmpty && lowBand.Length < vector.Length)
            {
                throw new ArgumentException("The CELT folding spectrum is shorter than the partition.", nameof(lowBand));
            }
            if (!float.IsFinite(gain) || gain is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(gain)); }
            _ = CeltSpreadingRotation.GetFactor(state.Spreading);
            return ReadCore(ref decoder, ref state, vector, budgetEighths, blocks, lowBand, frameShift, gain, fill);
        }

        private static uint ReadCore(ref OpusRangeDecoder decoder, ref CeltBandDecoderState state, scoped Span<float> vector,
            int budget, int blocks, scoped ReadOnlySpan<float> lowBand, int frameShift, float gain, uint fill)
        {
            if (frameShift >= 0 && vector.Length > 2 && budget > CeltPulseAllocation.MaximumCost(state.Band, frameShift) + 11)
            {
                int originalBlocks = blocks;
                int half = vector.Length / 2;
                if (blocks == 1) { fill = (fill & 1) | (fill << 1); }
                blocks = (blocks + 1) / 2;
                frameShift--;
                CeltSplitMode mode = new(state.Band, frameShift, half, blocks, originalBlocks, false, CeltBandTables.BandCount);
                CeltSplitAngle split = CeltSplitAngleReader.Read(ref decoder, mode, budget, state.RemainingEighths, fill);
                budget -= split.EighthBitsUsed;
                state.RemainingEighths -= split.EighthBitsUsed;
                fill = split.Fill;
                int delta = split.AllocationDelta;
                if (originalBlocks > 1 && split.Angle is > 0 and < 16384)
                {
                    if (split.Angle > 8192) { delta -= delta >> (4 - frameShift); }
                    else { delta = Math.Min(0, delta + ((half * 8) >> (5 - frameShift))); }
                }
                int midBudget = Math.Max(0, Math.Min(budget, (budget - delta) / 2));
                int sideBudget = budget - midBudget;
                scoped ReadOnlySpan<float> midFold = default, sideFold = default;
                if (!lowBand.IsEmpty)
                {
                    midFold = lowBand[..half];
                    sideFold = lowBand.Slice(half, half);
                }
                int before = state.RemainingEighths;
                uint midMask, sideMask;
                if (midBudget >= sideBudget)
                {
                    midMask = ReadCore(ref decoder, ref state, vector[..half], midBudget, blocks, midFold, frameShift,
                        gain * (split.MidGain * (1f / 32768)), fill);
                    int unused = midBudget - (before - state.RemainingEighths);
                    if (unused > 24 && split.Angle != 0) { sideBudget += unused - 24; }
                    sideMask = ReadCore(ref decoder, ref state, vector[half..], sideBudget, blocks, sideFold, frameShift,
                        gain * (split.SideGain * (1f / 32768)), fill >> blocks);
                }
                else
                {
                    sideMask = ReadCore(ref decoder, ref state, vector[half..], sideBudget, blocks, sideFold, frameShift,
                        gain * (split.SideGain * (1f / 32768)), fill >> blocks);
                    int unused = sideBudget - (before - state.RemainingEighths);
                    if (unused > 24 && split.Angle != 16384) { midBudget += unused - 24; }
                    midMask = ReadCore(ref decoder, ref state, vector[..half], midBudget, blocks, midFold, frameShift,
                        gain * (split.MidGain * (1f / 32768)), fill);
                }
                return midMask | (sideMask << (originalBlocks / 2));
            }
            int pseudo = CeltPulseAllocation.Select(state.Band, frameShift, budget);
            int cost = CeltPulseAllocation.Cost(state.Band, frameShift, pseudo);
            state.RemainingEighths -= cost;
            while (state.RemainingEighths < 0 && pseudo > 0)
            {
                state.RemainingEighths += cost;
                cost = CeltPulseAllocation.Cost(state.Band, frameShift, --pseudo);
                state.RemainingEighths -= cost;
            }
            if (pseudo != 0)
            {
                return CeltPulseVector.Read(state.Codebook, ref decoder, vector, CeltPulseAllocation.PulseCount(pseudo),
                    blocks, state.Spreading, gain);
            }
            uint mask = (1u << blocks) - 1;
            fill &= mask;
            if (fill == 0)
            {
                vector.Clear();
                return 0;
            }
            for (int at = 0; at < vector.Length; at++)
            {
                state.Seed = CeltNoise.Next(state.Seed);
                if (lowBand.IsEmpty) { vector[at] = (int)state.Seed >> 20; }
                else if ((state.Seed & 0x8000) != 0) { vector[at] = lowBand[at] + (1f / 256); }
                else { vector[at] = lowBand[at] - (1f / 256); }
            }
            CeltVectorMath.Normalize(vector, gain);
            if (lowBand.IsEmpty) { return mask; }
            return fill;
        }

    }
}
