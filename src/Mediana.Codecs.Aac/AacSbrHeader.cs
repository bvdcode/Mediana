// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal record AacSbrHeader(bool AmplitudeResolution, int StartFrequency, int StopFrequency, int CrossoverBand,
        int FrequencyScale, bool AlternateScale, int NoiseBands, int LimiterBands, int LimiterGains,
        bool InterpolateFrequency, bool SmoothingMode)
    {
        public static AacSbrHeader Read(ref AacBitReader bits)
        {
            bool amplitudeResolution = bits.Read(1) != 0;
            int start = bits.Read(4);
            int stop = bits.Read(4);
            int crossover = bits.Read(3);
            bits.Skip(2);
            bool firstExtra = bits.Read(1) != 0;
            bool secondExtra = bits.Read(1) != 0;
            int scale = 2;
            bool alternate = true;
            int noise = 2;
            int limiterBands = 2;
            int limiterGains = 2;
            bool interpolate = true;
            bool smoothing = true;
            if (firstExtra)
            {
                scale = bits.Read(2);
                alternate = bits.Read(1) != 0;
                noise = bits.Read(2);
            }
            if (secondExtra)
            {
                limiterBands = bits.Read(2);
                limiterGains = bits.Read(2);
                interpolate = bits.Read(1) != 0;
                smoothing = bits.Read(1) != 0;
            }
            return new(amplitudeResolution, start, stop, crossover, scale, alternate, noise,
                limiterBands, limiterGains, interpolate, smoothing);
        }

        public bool ChangesFrequencyTables(AacSbrHeader previous) =>
            StartFrequency != previous.StartFrequency || StopFrequency != previous.StopFrequency ||
            CrossoverBand != previous.CrossoverBand || FrequencyScale != previous.FrequencyScale ||
            AlternateScale != previous.AlternateScale || NoiseBands != previous.NoiseBands;
    }
}
