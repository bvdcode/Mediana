// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacSpectralTools
    {
        public static void Reconstruct(AacChannelState left, AacChannelState? right, bool commonWindow,
            int maskMode, ReadOnlySpan<byte> mask, ref uint noiseState, CancellationToken cancellationToken)
        {
            InverseQuantize(left);
            if (right is not null)
            {
                InverseQuantize(right);
            }
            if (right is null || !commonWindow)
            {
                ApplyIndependentNoise(left, ref noiseState);
                if (right is not null)
                {
                    ApplyIndependentNoise(right, ref noiseState);
                }
            }
            else
            {
                ApplyJointTools(left, right, maskMode, mask, ref noiseState, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            AacTemporalNoiseShaping.Apply(left);
            if (right is not null)
            {
                AacTemporalNoiseShaping.Apply(right);
            }
        }

        private static void InverseQuantize(AacChannelState channel)
        {
            AacWindowInfo info = channel.Info;
            ReadOnlySpan<int> quantized = channel.Quantized;
            Span<double> spectrum = channel.Spectrum;
            ReadOnlySpan<byte> books = channel.Books;
            ReadOnlySpan<int> scales = channel.Scales;
            int firstWindow = 0;
            for (int group = 0; group < info.Groups.Length; group++)
            {
                for (int band = 0; band < info.MaxBands; band++)
                {
                    int index = group * 64 + band;
                    if (books[index] is < 1 or > 11)
                    {
                        continue;
                    }
                    double gain = AacQuantizer.Gain(scales[index]);
                    for (int window = 0; window < info.Groups[group]; window++)
                    {
                        int start = (firstWindow + window) * info.WindowSize + info.Bands[band];
                        int end = (firstWindow + window) * info.WindowSize + info.Bands[band + 1];
                        for (int bin = start; bin < end; bin++)
                        {
                            int value = quantized[bin];
                            spectrum[bin] = Math.Sign(value) * AacQuantizer.Magnitude(Math.Abs(value)) * gain;
                        }
                    }
                }
                firstWindow += info.Groups[group];
            }
        }

        private static void ApplyIndependentNoise(AacChannelState channel, ref uint noiseState)
        {
            AacWindowInfo info = channel.Info;
            int firstWindow = 0;
            for (int group = 0; group < info.Groups.Length; group++)
            {
                for (int band = 0; band < info.MaxBands; band++)
                {
                    int index = group * 64 + band;
                    if (channel.Books[index] != 13)
                    {
                        continue;
                    }
                    int width = info.Bands[band + 1] - info.Bands[band];
                    for (int window = 0; window < info.Groups[group]; window++)
                    {
                        int start = (firstWindow + window) * info.WindowSize + info.Bands[band];
                        Noise(channel.Spectrum.Slice(start, width), channel.Scales[index], ref noiseState);
                    }
                }
                firstWindow += info.Groups[group];
            }
        }

        private static void ApplyJointTools(AacChannelState left, AacChannelState right, int maskMode,
            ReadOnlySpan<byte> mask, ref uint noiseState, CancellationToken cancellationToken)
        {
            AacWindowInfo info = left.Info;
            int firstWindow = 0;
            for (int group = 0; group < info.Groups.Length; group++)
            {
                for (int band = 0; band < info.MaxBands; band++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int index = group * 64 + band;
                    bool midSide = maskMode switch
                    {
                        0 => false,
                        1 => mask[index] != 0,
                        2 => true,
                        _ => throw new InvalidDataException("The AAC stereo mask mode is reserved.")
                    };
                    int leftBook = left.Books[index];
                    int rightBook = right.Books[index];
                    if (maskMode == 2 && rightBook is 14 or 15)
                    {
                        throw new InvalidDataException("AAC intensity stereo cannot use the all-ones M/S mask.");
                    }
                    if (midSide && (leftBook == 13) != (rightBook == 13))
                    {
                        throw new InvalidDataException("An AAC M/S noise band must use noise substitution in both channels.");
                    }
                    int width = info.Bands[band + 1] - info.Bands[band];
                    for (int window = 0; window < info.Groups[group]; window++)
                    {
                        int start = (firstWindow + window) * info.WindowSize + info.Bands[band];
                        Span<double> leftBand = left.Spectrum.Slice(start, width);
                        Span<double> rightBand = right.Spectrum.Slice(start, width);
                        uint correlatedState = noiseState;
                        if (leftBook == 13)
                        {
                            Noise(leftBand, left.Scales[index], ref noiseState);
                        }
                        if (rightBook == 13)
                        {
                            if (leftBook == 13 && midSide)
                            {
                                Noise(rightBand, right.Scales[index], ref correlatedState);
                            }
                            else
                            {
                                Noise(rightBand, right.Scales[index], ref noiseState);
                            }
                        }
                        if (rightBook is 14 or 15)
                        {
                            int sign = rightBook switch
                            {
                                14 => -1,
                                15 => 1,
                                _ => throw new InvalidOperationException("The intensity codebook is invalid.")
                            };
                            if (maskMode == 1 && midSide)
                            {
                                sign = -sign;
                            }
                            double scale = sign * Math.Pow(2, -0.25 * right.Scales[index]);
                            RequireFinite(scale);
                            for (int bin = 0; bin < width; bin++)
                            {
                                rightBand[bin] = leftBand[bin] * scale;
                            }
                        }
                        else if (midSide && leftBook != 13 && rightBook != 13)
                        {
                            for (int bin = 0; bin < width; bin++)
                            {
                                double mid = leftBand[bin];
                                double side = rightBand[bin];
                                leftBand[bin] = mid + side;
                                rightBand[bin] = mid - side;
                            }
                        }
                    }
                }
                firstWindow += info.Groups[group];
            }
        }

        private static void Noise(Span<double> spectrum, int energy, ref uint state)
        {
            double sum = 0;
            for (int index = 0; index < spectrum.Length; index++)
            {
                state = unchecked(state * 1664525 + 1013904223);
                double value = unchecked((int)state) / 2147483648.0;
                spectrum[index] = value;
                sum += value * value;
            }
            double scale = Math.Pow(2, 0.25 * energy) / Math.Sqrt(sum);
            RequireFinite(scale);
            for (int index = 0; index < spectrum.Length; index++)
            {
                spectrum[index] *= scale;
            }
        }

        private static void RequireFinite(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidDataException("The AAC scale produces a nonfinite spectral value.");
            }
        }
    }
}
