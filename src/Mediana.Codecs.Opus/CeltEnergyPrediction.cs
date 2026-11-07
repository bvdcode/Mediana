// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly ref struct CeltEnergyPrediction(ReadOnlySpan<float> previous, ReadOnlySpan<float> previous2, int lostDuration)
    {
        private readonly ReadOnlySpan<float> previous = previous;
        private readonly ReadOnlySpan<float> previous2 = previous2;

        public void Recover(Span<float> energies, CeltBandMode mode, bool intra)
        {
            if (intra || lostDuration == 0) { return; }
            int missing = Math.Min(10, lostDuration >> mode.FrameShift);
            float safety = mode.FrameShift switch { 0 => 1.5f, 1 => .5f, 2 or 3 => 0, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
            for (int channel = 0; channel < 2; channel++)
            {
                for (int band = mode.FirstBand; band < mode.EndBand; band++)
                {
                    int at = channel * CeltBandTables.BandCount + band;
                    float current = energies[at], first = previous[at], second = previous2[at];
                    if (current < Math.Max(first, second))
                    {
                        float slope = Math.Min(2, Math.Max(first - current, .5f * (second - current)));
                        current -= Math.Max(0, (1 + missing) * slope);
                        energies[at] = Math.Max(-20, current);
                    }
                    else { energies[at] = Math.Min(current, Math.Min(first, second)); }
                    energies[at] -= safety;
                }
            }
        }
    }
}
