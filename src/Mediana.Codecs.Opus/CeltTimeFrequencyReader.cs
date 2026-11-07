// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltTimeFrequencyReader
    {
        public static void Read(ref OpusRangeDecoder decoder, CeltBandMode mode, bool transient, scoped Span<int> resolutions)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(resolutions.Length, mode.EndBand, nameof(resolutions));
            int firstCost = 4, nextCost = 5, tableOffset = mode.FrameShift * 8;
            if (transient) { firstCost = 2; nextCost = 4; tableOffset += 4; }
            int budget = decoder.FrameBitCount;
            bool reserveSelection = mode.FrameShift > 0 && decoder.BitsUsed + firstCost + 1 <= budget;
            if (reserveSelection) { budget--; }
            int current = 0, changed = 0, cost = firstCost;
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                if (decoder.BitsUsed + cost <= budget)
                {
                    if (decoder.ReadBit(cost)) { current ^= 1; }
                    changed |= current;
                }
                resolutions[band] = current;
                cost = nextCost;
            }
            if (reserveSelection && Selection[tableOffset + changed] != Selection[tableOffset + 2 + changed] && decoder.ReadBit(1))
            {
                tableOffset += 2;
            }
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                resolutions[band] = Selection[tableOffset + resolutions[band]];
            }
        }

        private static ReadOnlySpan<sbyte> Selection =>
        [
            0, -1, 0, -1, 0, -1, 0, -1,
            0, -1, 0, -2, 1,  0, 1, -1,
            0, -2, 0, -3, 2,  0, 1, -1,
            0, -2, 0, -3, 3,  0, 1, -1
        ];
    }
}
