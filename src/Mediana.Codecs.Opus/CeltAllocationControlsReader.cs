// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltAllocationControlsReader
    {
        public static CeltAllocationControls Read(ref OpusRangeDecoder decoder, CeltBandMode mode,
            bool transient, scoped Span<int> boosts)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(boosts.Length, mode.EndBand, nameof(boosts));
            CeltSpreading spreading = CeltSpreading.Normal;
            if (decoder.BitsUsed + 4 <= decoder.FrameBitCount)
            {
                spreading = decoder.ReadInverseCdf([25, 23, 2, 0], 5) switch
                {
                    0 => CeltSpreading.None,
                    1 => CeltSpreading.Light,
                    2 => CeltSpreading.Normal,
                    3 => CeltSpreading.Aggressive,
                    _ => throw new InvalidDataException("The CELT spreading symbol is invalid.")
                };
            }
            int frameBudget = checked(decoder.FrameBitCount * 8);
            int unboosted = frameBudget, cost = 6;
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                int width = mode.Channels * (CeltBandTables.Width(band) << mode.FrameShift);
                int quantum = Math.Min(width * 8, Math.Max(48, width));
                int cap = CeltBandTables.MaximumAllocation(mode, band);
                int boost = 0, repeatedCost = cost;
                while (decoder.EighthBitsUsed + repeatedCost * 8 < unboosted && boost < cap)
                {
                    if (!decoder.ReadBit(repeatedCost)) { break; }
                    boost += quantum;
                    unboosted -= quantum;
                    repeatedCost = 1;
                }
                boosts[band] = boost;
                if (boost > 0) { cost = Math.Max(2, cost - 1); }
            }
            int trim = 5;
            if (decoder.EighthBitsUsed + 48 <= unboosted)
            {
                trim = decoder.ReadInverseCdf([126, 124, 119, 109, 87, 41, 19, 9, 4, 2, 0], 7);
            }
            int available = frameBudget - decoder.EighthBitsUsed - 1;
            int antiCollapse = 0;
            if (transient && mode.FrameShift >= 2 && available >= (mode.FrameShift + 2) * 8) { antiCollapse = 8; }
            return new(spreading, trim, available - antiCollapse, antiCollapse);
        }
    }
}
