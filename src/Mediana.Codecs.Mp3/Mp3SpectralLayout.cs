// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3SpectralLayout
    {
        public static int Write(int sampleRate, Mp3GranuleInfo info, Span<Mp3SpectralBand> bands)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bands.Length, Mp3ScaleFactors.Capacity);
            ReadOnlySpan<int> longBands = Mp3ScaleFactorBands.Long(sampleRate);
            ReadOnlySpan<int> shortBands = Mp3ScaleFactorBands.Short(sampleRate);
            int longCount = info.BlockType != 2 ? 22 : info.MixedBlock ? (sampleRate >= 32000 ? 8 : 6) : 0;
            int offset = 0;
            int count = 0;
            for (int band = 0; band < longCount; band++)
            {
                int width = longBands[band + 1] - longBands[band];
                bands[count++] = new Mp3SpectralBand(offset, width, band, -1, longBands[band]);
                offset += width;
            }
            if (info.BlockType == 2)
            {
                for (int band = info.MixedBlock ? 3 : 0; band < 13; band++)
                {
                    int width = shortBands[band + 1] - shortBands[band];
                    for (int window = 0; window < 3; window++)
                    {
                        bands[count++] = new Mp3SpectralBand(offset, width, band, window, shortBands[band]);
                        offset += width;
                    }
                }
            }
            if (offset != Mp3ScaleFactorBands.SpectrumLength)
            {
                throw new InvalidOperationException("The MP3 spectral layout does not cover a complete granule.");
            }
            return count;
        }

        public static void Reorder(ReadOnlySpan<double> input, ReadOnlySpan<Mp3SpectralBand> bands, Span<double> output)
        {
            foreach (Mp3SpectralBand band in bands)
            {
                if (band.Window < 0)
                {
                    input.Slice(band.Offset, band.Width).CopyTo(output.Slice(band.Offset, band.Width));
                    continue;
                }
                for (int index = 0; index < band.Width; index++)
                {
                    int frequency = band.FrequencyStart + index;
                    output[frequency / 6 * 18 + band.Window * 6 + frequency % 6] = input[band.Offset + index];
                }
            }
        }
    }
}
