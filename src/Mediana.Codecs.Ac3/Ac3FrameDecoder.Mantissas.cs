// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal partial class Ac3FrameDecoder
    {
        private void ReadMantissas(ref Ac3BitReader bits, int block)
        {
            Ac3MantissaReader mantissas = new();
            bool couplingRead = false;
            for (int ch = 0; ch < fullChannels; ch++)
            {
                ReadSpectrum(ref bits, ref mantissas, ch, block, 0, end[ch], dither[ch]);
                if (coupled[ch] && !couplingRead)
                {
                    ReadSpectrum(ref bits, ref mantissas, 6, block, couplingStart, couplingEnd, false);
                    couplingRead = true;
                }
            }
            if (header.HasLfe) { ReadSpectrum(ref bits, ref mantissas, fullChannels, block, 0, 7, false); }
            if (coupling)
            {
                ReadOnlySpan<byte> bap = workspace.Bap(6), exponents = workspace.Exponents(6);
                for (int ch = 0; ch < fullChannels; ch++)
                {
                    if (!coupled[ch]) { continue; }
                    Span<double> spectrum = workspace.Spectrum(ch);
                    ReadOnlySpan<double> common = workspace.Spectrum(6), coords = workspace.Coordinates(ch);
                    if (enhancedCouplingInUse)
                    {
                        enhancedCoordinates.Process(ch, spectrum, common, bap, exponents, dither[ch], ref random);
                        continue;
                    }
                    for (int bin = couplingStart; bin < couplingEnd; bin++)
                    {
                        double coefficient = common[bin];
                        if (bap[bin] == 0 && dither[ch]) { coefficient = Ac3MantissaReader.ApplyExponent(NextDither(), exponents[bin]); }
                        spectrum[bin] = coefficient * coords[(bin - couplingStart) / 12];
                    }
                }
            }
        }

        private void ReadSpectrum(ref Ac3BitReader bits, ref Ac3MantissaReader mantissas, int channel, int block, int start, int finish, bool useDither)
        {
            Span<double> spectrum = workspace.Spectrum(channel); spectrum.Clear();
            ReadOnlySpan<byte> exponents = workspace.Exponents(channel), bap = workspace.Bap(channel);
            if (adaptiveTransform.IsEnabled(channel))
            {
                adaptiveTransform.ReadSpectrum(ref bits, channel, block, start, finish, bap, exponents, spectrum, ref random);
                return;
            }
            for (int bin = start; bin < finish; bin++)
            {
                double value = bap[bin] == 0 && useDither ? NextDither() : mantissas.Read(ref bits, bap[bin]);
                spectrum[bin] = Ac3MantissaReader.ApplyExponent(value, exponents[bin]);
            }
        }

        private double NextDither()
        {
            return Ac3NoiseGenerator.Next(ref random) * 0.7071067811865476;
        }

        private void ApplyRematrix()
        {
            if (header.ChannelMode != 2) { return; }
            ReadOnlySpan<int> boundaries = [13, 25, 37, 61, 253];
            Span<double> left = workspace.Spectrum(0), right = workspace.Spectrum(1);
            int limit = Math.Min(end[0], end[1]);
            for (int band = 0; band < 4; band++)
            {
                if (!rematrix[band]) { continue; }
                for (int bin = boundaries[band]; bin < Math.Min(boundaries[band + 1], limit); bin++)
                {
                    double first = left[bin], second = right[bin]; left[bin] = first + second; right[bin] = first - second;
                }
            }
        }
    }
}
