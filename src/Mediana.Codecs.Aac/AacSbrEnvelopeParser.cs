// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacSbrEnvelopeParser
    {
        public static void ReadEnvelope(ref AacBitReader bits, AacSbrChannelData data, AacSbrFrequencyTables tables,
            AacSbrHeader header, bool balance)
        {
            data.CoarseAmplitude = header.AmplitudeResolution && !(data.Grid.FrameClass == 0 && data.Grid.EnvelopeCount == 1);
            (AacSbrCodebook Frequency, AacSbrCodebook Time, int StartBits) books = (data.CoarseAmplitude, balance) switch
            {
                (false, false) => (AacSbrCodebook.EnvelopeFineFrequency, AacSbrCodebook.EnvelopeFineTime, 7),
                (false, true) => (AacSbrCodebook.BalanceFineFrequency, AacSbrCodebook.BalanceFineTime, 6),
                (true, false) => (AacSbrCodebook.EnvelopeCoarseFrequency, AacSbrCodebook.EnvelopeCoarseTime, 6),
                (true, true) => (AacSbrCodebook.BalanceCoarseFrequency, AacSbrCodebook.BalanceCoarseTime, 5)
            };
            int multiplier = balance ? 2 : 1;
            for (int envelope = 0; envelope < data.Grid.EnvelopeCount; envelope++)
            {
                bool time = data.EnvelopeTimeDirection[envelope];
                if (time && !data.HasPreviousEnvelope)
                {
                    throw new InvalidDataException("The SBR envelope requires an unavailable previous frame.");
                }
                ReadOnlySpan<int> borders = data.Grid.HighResolution[envelope] ? tables.High : tables.Low;
                int value = 0;
                for (int band = 0; band < borders.Length - 1; band++)
                {
                    int highBand = FindBand(tables.High, borders[band]);
                    if (time)
                    {
                        value = data.PreviousEnvelope[highBand] + multiplier * AacSbrHuffman.Read(ref bits, books.Time);
                    }
                    else
                    {
                        value = band == 0 ? multiplier * bits.Read(books.StartBits) :
                            value + multiplier * AacSbrHuffman.Read(ref bits, books.Frequency);
                    }
                    if (value < 0 || value > (data.CoarseAmplitude ? 35 : 70))
                    {
                        throw new InvalidDataException("The SBR envelope energy is outside its valid range.");
                    }
                    data.Envelope[envelope * 64 + band] = value;
                    data.Energy[envelope * 64 + band] = Math.Pow(2, 4 + value * (data.CoarseAmplitude ? 1 : 0.5));
                    for (int high = highBand; high < tables.HighCount && tables.High[high] < borders[band + 1]; high++)
                    {
                        data.PreviousEnvelope[high] = value;
                    }
                }
                data.HasPreviousEnvelope = true;
            }
        }

        public static void ReadNoise(ref AacBitReader bits, AacSbrChannelData data, AacSbrFrequencyTables tables, bool balance)
        {
            AacSbrCodebook timeBook = balance ? AacSbrCodebook.NoiseBalanceTime : AacSbrCodebook.NoiseTime;
            AacSbrCodebook frequencyBook = balance ? AacSbrCodebook.BalanceCoarseFrequency : AacSbrCodebook.EnvelopeCoarseFrequency;
            int multiplier = balance ? 2 : 1;
            for (int envelope = 0; envelope < data.Grid.NoiseEnvelopeCount; envelope++)
            {
                bool time = data.NoiseTimeDirection[envelope];
                if (time && !data.HasPreviousNoise)
                {
                    throw new InvalidDataException("The SBR noise floor requires an unavailable previous frame.");
                }
                int value = 0;
                for (int band = 0; band < tables.NoiseCount; band++)
                {
                    value = time ? data.PreviousNoise[band] + multiplier * AacSbrHuffman.Read(ref bits, timeBook) :
                        band == 0 ? multiplier * bits.Read(5) : value + multiplier * AacSbrHuffman.Read(ref bits, frequencyBook);
                    if (value < 0 || value > 35)
                    {
                        throw new InvalidDataException("The SBR noise floor is outside its valid range.");
                    }
                    data.PreviousNoise[band] = value;
                    data.Noise[envelope * 5 + band] = value;
                    data.NoiseRatio[envelope * 5 + band] = Math.Pow(2, 6 - value);
                }
                data.HasPreviousNoise = true;
            }
        }

        public static void UnmapCoupledChannels(AacSbrChannelData left, AacSbrChannelData right, AacSbrFrequencyTables tables)
        {
            for (int envelope = 0; envelope < left.Grid.EnvelopeCount; envelope++)
            {
                int count = left.Grid.HighResolution[envelope] ? tables.HighCount : tables.LowCount;
                for (int band = 0; band < count; band++)
                {
                    int index = envelope * 64 + band;
                    double total = 2 * left.Energy[index];
                    double pan = Math.Pow(2, right.Envelope[index] * (right.CoarseAmplitude ? 1 : 0.5) - 12);
                    right.Energy[index] = total / (1 + pan);
                    left.Energy[index] = pan * right.Energy[index];
                }
            }
            for (int envelope = 0; envelope < left.Grid.NoiseEnvelopeCount; envelope++)
            {
                for (int band = 0; band < tables.NoiseCount; band++)
                {
                    int index = envelope * 5 + band;
                    double total = 2 * left.NoiseRatio[index];
                    double pan = Math.Pow(2, right.Noise[index] - 12);
                    right.NoiseRatio[index] = total / (1 + pan);
                    left.NoiseRatio[index] = pan * right.NoiseRatio[index];
                }
            }
        }

        private static int FindBand(ReadOnlySpan<int> borders, int frequency)
        {
            for (int index = 0; index < borders.Length - 1; index++)
            {
                if (borders[index] == frequency)
                {
                    return index;
                }
            }
            throw new InvalidDataException("The SBR frequency resolution cannot be mapped.");
        }
    }
}
