// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacTemporalNoiseShaping
    {
        public static void Read(ref AacBitReader bits, AacChannelState channel)
        {
            AacWindowInfo info = channel.Info;
            int windowCount = info.ShortWindow ? 8 : 1;
            int limit = Math.Min(info.MaxBands, AacBandTables.TnsLimit(info.FrequencyIndex, info.ShortWindow));
            Span<double> previous = stackalloc double[13];
            for (int window = 0; window < windowCount; window++)
            {
                int filters = bits.Read(info.ShortWindow ? 1 : 2);
                int resolution = filters > 0 ? bits.Read(1) + 3 : 0;
                int bottom = info.Bands.Length - 1;
                for (int index = 0; index < filters; index++)
                {
                    int top = bottom;
                    bottom = Math.Max(0, top - bits.Read(info.ShortWindow ? 4 : 6));
                    int order = bits.Read(info.ShortWindow ? 3 : 5);
                    if (order == 0)
                    {
                        continue;
                    }
                    if (order > (info.ShortWindow ? 7 : 12))
                    {
                        throw new InvalidDataException("The AAC TNS order exceeds the LC profile limit.");
                    }
                    bool backward = bits.Read(1) != 0;
                    int coefficientBits = resolution - bits.Read(1);
                    double[] coefficients = new double[order + 1];
                    coefficients[0] = 1;
                    for (int degree = 1; degree <= order; degree++)
                    {
                        int value = bits.Read(coefficientBits);
                        if ((value & (1 << (coefficientBits - 1))) != 0)
                        {
                            value -= 1 << coefficientBits;
                        }
                        double divisor = (1 << (resolution - 1)) + (value < 0 ? 0.5 : -0.5);
                        double reflection = Math.Sin(value * (Math.PI / 2) / divisor);
                        coefficients.AsSpan().CopyTo(previous);
                        for (int coefficient = 1; coefficient < degree; coefficient++)
                        {
                            coefficients[coefficient] = previous[coefficient] + reflection * previous[degree - coefficient];
                        }
                        coefficients[degree] = reflection;
                    }
                    int start = window * info.WindowSize + info.Bands[Math.Min(bottom, limit)];
                    int end = window * info.WindowSize + info.Bands[Math.Min(top, limit)];
                    channel.TnsFilters.Add(new AacTnsFilter(start, end, backward, coefficients));
                }
            }
        }

        public static void Apply(AacChannelState channel)
        {
            Span<double> spectrum = channel.Spectrum;
            foreach (AacTnsFilter filter in channel.TnsFilters)
            {
                int count = filter.End - filter.Start;
                for (int index = 0; index < count; index++)
                {
                    int current = filter.Backward ? filter.End - index - 1 : filter.Start + index;
                    double sample = spectrum[current];
                    for (int delay = 1; delay < filter.Coefficients.Length && delay <= index; delay++)
                    {
                        int previous = filter.Backward ? current + delay : current - delay;
                        sample -= filter.Coefficients[delay] * spectrum[previous];
                    }
                    spectrum[current] = sample;
                }
            }
        }
    }
}
