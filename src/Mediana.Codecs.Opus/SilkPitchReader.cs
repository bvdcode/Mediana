// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkPitchReader
    {
        public static SilkPitchIndices Read(ref OpusRangeDecoder decoder, SilkFrameMode mode, SilkCodingMode coding,
            SilkSignalType previousSignal, short previousLagIndex, scoped Span<byte> filterIndices)
        {
            ReadOnlySpan<byte> contour = SilkPitchModels.ContourModel(mode);
            ArgumentOutOfRangeException.ThrowIfLessThan(filterIndices.Length, mode.Subframes, nameof(filterIndices));
            bool previousVoiced = previousSignal switch
            {
                SilkSignalType.Inactive or SilkSignalType.Unvoiced => false,
                SilkSignalType.Voiced => true,
                _ => throw new ArgumentOutOfRangeException(nameof(previousSignal))
            };
            bool deltaAllowed = coding switch
            {
                SilkCodingMode.Independent or SilkCodingMode.IndependentWithoutLtpScaling => false,
                SilkCodingMode.Conditional => previousVoiced,
                _ => throw new ArgumentOutOfRangeException(nameof(coding))
            };
            int lag = 0;
            bool absolute = true;
            if (deltaAllowed)
            {
                int delta = decoder.ReadInverseCdf(SilkPitchModels.Delta);
                if (delta > 0) { lag = previousLagIndex + delta - 9; absolute = false; }
            }
            if (absolute)
            {
                int high = decoder.ReadInverseCdf(SilkPitchModels.LagHigh);
                int low = mode.RateKhz switch
                {
                    8 => decoder.ReadInverseCdf([192, 128, 64, 0]),
                    12 => decoder.ReadInverseCdf([213, 171, 128, 85, 43, 0]),
                    16 => decoder.ReadInverseCdf([224, 192, 160, 128, 96, 64, 32, 0]),
                    _ => throw new InvalidOperationException()
                };
                lag = high * (mode.RateKhz / 2) + low;
            }
            byte contourIndex = (byte)decoder.ReadInverseCdf(contour);
            byte periodicity = (byte)decoder.ReadInverseCdf([179, 99, 0]);
            ReadOnlySpan<byte> filters = SilkLtpModels.Probability(periodicity);
            for (int at = 0; at < mode.Subframes; at++) { filterIndices[at] = (byte)decoder.ReadInverseCdf(filters); }
            byte scale = 0;
            if (coding == SilkCodingMode.Independent) { scale = (byte)decoder.ReadInverseCdf([128, 64, 0]); }
            return new(unchecked((short)lag), contourIndex, periodicity, scale);
        }

        public static int Dequantize(SilkFrameMode mode, SilkPitchIndices indices, ReadOnlySpan<byte> filterIndices,
            Span<int> lags, Span<short> coefficientsQ14)
        {
            ReadOnlySpan<sbyte> contours = SilkPitchModels.ContourLags(mode), filters = SilkLtpModels.Coefficients(indices.PeriodicityIndex);
            int codewords = contours.Length / mode.Subframes;
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indices.ContourIndex, codewords, nameof(indices));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(indices.ScaleIndex, 2, nameof(indices));
            ArgumentOutOfRangeException.ThrowIfLessThan(filterIndices.Length, mode.Subframes, nameof(filterIndices));
            ArgumentOutOfRangeException.ThrowIfLessThan(lags.Length, mode.Subframes, nameof(lags));
            ArgumentOutOfRangeException.ThrowIfLessThan(coefficientsQ14.Length, mode.Subframes * 5, nameof(coefficientsQ14));
            foreach (byte index in filterIndices[..mode.Subframes])
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, filters.Length / 5, nameof(filterIndices));
            }
            int minimum = 2 * mode.RateKhz, maximum = 18 * mode.RateKhz;
            for (int at = 0; at < mode.Subframes; at++)
            {
                lags[at] = Math.Clamp(minimum + indices.LagIndex + contours[at * codewords + indices.ContourIndex], minimum, maximum);
                for (int tap = 0; tap < 5; tap++) { coefficientsQ14[at * 5 + tap] = (short)(filters[filterIndices[at] * 5 + tap] << 7); }
            }
            return indices.ScaleIndex switch
            {
                0 => 15565,
                1 => 12288,
                2 => 8192,
                _ => throw new ArgumentOutOfRangeException(nameof(indices))
            };
        }
    }
}
