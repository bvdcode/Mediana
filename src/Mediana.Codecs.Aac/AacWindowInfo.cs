// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacWindowInfo(int frequencyIndex, int sequence, int shape, int maxBands, int[] groups)
    {
        public int FrequencyIndex { get; } = frequencyIndex;
        public int Sequence { get; } = sequence;
        public int Shape { get; } = shape;
        public int MaxBands { get; } = maxBands;
        public int[] Groups { get; } = groups;
        public bool ShortWindow => Sequence == 2;
        public int WindowSize => ShortWindow ? 128 : 1024;
        public ReadOnlySpan<int> Bands => AacBandTables.Get(FrequencyIndex, ShortWindow);

        public static AacWindowInfo Read(ref AacBitReader bits, int frequencyIndex)
        {
            if (bits.Read(1) != 0)
            {
                throw new InvalidDataException("The AAC reserved ICS bit must be zero.");
            }
            int sequence = bits.Read(2);
            int shape = bits.Read(1);
            int maxBands;
            int[] groups;
            if (sequence == 2)
            {
                maxBands = bits.Read(4);
                List<int> lengths = [1];
                for (int index = 0; index < 7; index++)
                {
                    if (bits.Read(1) != 0)
                    {
                        lengths[^1]++;
                    }
                    else
                    {
                        lengths.Add(1);
                    }
                }
                groups = lengths.ToArray();
            }
            else
            {
                maxBands = bits.Read(6);
                if (bits.Read(1) != 0)
                {
                    throw new InvalidDataException("Prediction data is not permitted in an AAC-LC ICS.");
                }
                groups = [1];
            }
            AacWindowInfo result = new(frequencyIndex, sequence, shape, maxBands, groups);
            if (maxBands >= result.Bands.Length)
            {
                throw new InvalidDataException("The AAC active bands exceed the sampling frequency band table.");
            }
            return result;
        }
    }
}
