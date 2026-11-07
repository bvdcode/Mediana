// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltBandDenormalizer
    {
        public static void Apply(CeltBandMode mode, ReadOnlySpan<float> normalized, ReadOnlySpan<float> energies,
            Span<float> frequency, bool silence)
        {
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            int multiplier = 1 << mode.FrameShift, samples = CeltBandTables.ShortTransformSize * multiplier;
            ArgumentOutOfRangeException.ThrowIfLessThan(normalized.Length, samples * mode.Channels, nameof(normalized));
            ArgumentOutOfRangeException.ThrowIfLessThan(frequency.Length, samples * mode.Channels, nameof(frequency));
            ArgumentOutOfRangeException.ThrowIfLessThan(energies.Length, CeltBandTables.BandCount * mode.Channels, nameof(energies));
            if (silence)
            {
                frequency[..(samples * mode.Channels)].Clear();
                return;
            }
            for (int channel = 0; channel < mode.Channels; channel++)
            {
                int start = channel * samples;
                frequency.Slice(start, CeltBandTables.Boundaries[mode.FirstBand] * multiplier).Clear();
                for (int band = mode.FirstBand; band < mode.EndBand; band++)
                {
                    float logarithm = energies[channel * CeltBandTables.BandCount + band] + MeanEnergies[band] * (1f / 16);
                    float gain = CeltEnergyMath.Exp2(Math.Min(32, logarithm));
                    int first = start + CeltBandTables.Boundaries[band] * multiplier;
                    int end = start + CeltBandTables.Boundaries[band + 1] * multiplier;
                    for (int at = first; at < end; at++) { frequency[at] = normalized[at] * gain; }
                }
                int limit = CeltBandTables.Boundaries[mode.EndBand] * multiplier;
                frequency.Slice(start + limit, samples - limit).Clear();
            }
        }

        private static ReadOnlySpan<byte> MeanEnergies =>
            [103, 100, 92, 85, 81, 77, 72, 70, 78, 75, 73, 71, 78, 74, 69, 72, 70, 74, 76, 71, 60];
    }
}
