// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltAntiCollapse
    {
        public static void Apply(CeltBandMode mode, scoped CeltSpectralBuffers buffers, ReadOnlySpan<float> energies,
            ReadOnlySpan<float> previous, ReadOnlySpan<float> previous2, ReadOnlySpan<int> shapeEighths, uint seed)
        {
            if (mode.Channels is not (1 or 2) || mode.FrameShift is < 2 or > 3)
            {
                throw new ArgumentException("CELT anti-collapse requires a transient ten- or twenty-millisecond frame.", nameof(mode));
            }
            int blocks = 1 << mode.FrameShift, samples = CeltBandTables.ShortTransformSize * blocks;
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Coefficients.Length, samples * mode.Channels, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.CollapseMasks.Length, CeltBandTables.BandCount * mode.Channels, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(energies.Length, CeltBandTables.BandCount * mode.Channels, nameof(energies));
            ArgumentOutOfRangeException.ThrowIfLessThan(previous.Length, CeltBandTables.BandCount * 2, nameof(previous));
            ArgumentOutOfRangeException.ThrowIfLessThan(previous2.Length, CeltBandTables.BandCount * 2, nameof(previous2));
            ArgumentOutOfRangeException.ThrowIfLessThan(shapeEighths.Length, mode.EndBand, nameof(shapeEighths));
            foreach (int allocation in shapeEighths[mode.FirstBand..mode.EndBand])
            {
                ArgumentOutOfRangeException.ThrowIfNegative(allocation, nameof(shapeEighths));
            }
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                int width = CeltBandTables.Width(band);
                int depth = ((1 + shapeEighths[band]) / width) >> mode.FrameShift;
                float threshold = .5f * CeltEnergyMath.Exp2(-.125f * depth);
                float normalization = 1f / MathF.Sqrt(width * blocks);
                for (int channel = 0; channel < mode.Channels; channel++)
                {
                    int energyAt = channel * CeltBandTables.BandCount + band;
                    float first = previous[energyAt], second = previous2[energyAt];
                    if (mode.Channels == 1)
                    {
                        first = Math.Max(first, previous[CeltBandTables.BandCount + band]);
                        second = Math.Max(second, previous2[CeltBandTables.BandCount + band]);
                    }
                    float difference = Math.Max(0, energies[energyAt] - Math.Min(first, second));
                    float level = 2 * CeltEnergyMath.Exp2(-difference);
                    if (mode.FrameShift == 3) { level *= 1.41421356f; }
                    level = Math.Min(threshold, level) * normalization;
                    Span<float> vector = buffers.Coefficients.Slice(channel * samples + CeltBandTables.Boundaries[band] * blocks, width * blocks);
                    int occupied = buffers.CollapseMasks[band * mode.Channels + channel];
                    bool changed = false;
                    for (int block = 0; block < blocks; block++)
                    {
                        if ((occupied & (1 << block)) != 0) { continue; }
                        for (int at = 0; at < width; at++)
                        {
                            seed = CeltNoise.Next(seed);
                            if ((seed & 0x8000) != 0) { vector[at * blocks + block] = level; }
                            else { vector[at * blocks + block] = -level; }
                        }
                        changed = true;
                    }
                    if (changed) { CeltVectorMath.Normalize(vector, 1); }
                }
            }
        }
    }
}
