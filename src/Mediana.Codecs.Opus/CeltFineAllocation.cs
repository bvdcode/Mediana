// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltFineAllocation
    {
        public static int Apply(CeltBandMode mode, scoped CeltBandAllocation allocation,
            scoped ReadOnlySpan<int> caps, int coded, int intensity, bool dual)
        {
            int balance = 0;
            for (int band = mode.FirstBand; band < coded; band++)
            {
                int dimensions = CeltBandTables.Width(band) << mode.FrameShift;
                int budget = allocation.ShapeEighthBits[band] + balance;
                int shape, fine = 0, priority = 1, excess;
                if (dimensions == 1)
                {
                    shape = Math.Min(budget, mode.Channels * 8);
                    excess = budget - shape;
                }
                else
                {
                    excess = Math.Max(0, budget - caps[band]);
                    shape = budget - excess;
                    int degrees = mode.Channels * dimensions;
                    if (mode.Channels == 2 && dimensions > 2 && !dual && band < intensity) { degrees++; }
                    int logarithm = degrees * (CeltBandTables.WidthLog2Eighths[band] + mode.FrameShift * 8);
                    int offset = (logarithm >> 1) - degrees * 21;
                    if (dimensions == 2) { offset += degrees * 2; }
                    if (shape + offset < degrees * 16) { offset += logarithm >> 2; }
                    else if (shape + offset < degrees * 24) { offset += logarithm >> 3; }
                    fine = Math.Max(0, shape + offset + degrees * 4) / (degrees * 8);
                    fine = Math.Min(fine, Math.Min(shape / (mode.Channels * 8), CeltEnergyModels.MaximumFineBits));
                    priority = 0;
                    if (fine * degrees * 8 >= shape + offset) { priority = 1; }
                    shape -= mode.Channels * fine * 8;
                }
                if (excess > 0)
                {
                    int extra = Math.Min(excess / (mode.Channels * 8), CeltEnergyModels.MaximumFineBits - fine);
                    fine += extra;
                    int consumed = extra * mode.Channels * 8;
                    priority = 0;
                    if (consumed >= excess - balance) { priority = 1; }
                    excess -= consumed;
                }
                allocation.ShapeEighthBits[band] = shape;
                allocation.FineBits[band] = fine;
                allocation.Priorities[band] = (byte)priority;
                balance = excess;
            }
            for (int band = coded; band < mode.EndBand; band++)
            {
                int fine = allocation.ShapeEighthBits[band] / (mode.Channels * 8);
                allocation.ShapeEighthBits[band] = 0;
                allocation.FineBits[band] = fine;
                allocation.Priorities[band] = 0;
                if (fine < 1) { allocation.Priorities[band] = 1; }
            }
            return balance;
        }
    }
}
