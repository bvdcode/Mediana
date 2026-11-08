// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacSbrFrequencyTables
    {
        private readonly int[] master = new int[65];
        private readonly int[] high = new int[65];
        private readonly int[] low = new int[33];
        private readonly int[] noise = new int[6];

        public int MasterCount { get; private set; }
        public int HighCount { get; private set; }
        public int LowCount { get; private set; }
        public int NoiseCount { get; private set; }
        public int StartBand { get; private set; }
        public int CrossoverBand => high[0];
        public int StopBand => high[HighCount];
        public ReadOnlySpan<int> Master => master.AsSpan(0, MasterCount + 1);
        public ReadOnlySpan<int> High => high.AsSpan(0, HighCount + 1);
        public ReadOnlySpan<int> Low => low.AsSpan(0, LowCount + 1);
        public ReadOnlySpan<int> Noise => noise.AsSpan(0, NoiseCount + 1);

        public void Update(AacSbrHeader header, int sampleRate)
        {
            StartBand = GetStartBand(header.StartFrequency, sampleRate);
            int stop = GetStopBand(header.StopFrequency, sampleRate, StartBand);
            int maximumWidth = sampleRate switch
            {
                >= 48000 => 32,
                44100 => 35,
                > 0 => 48,
                _ => throw new ArgumentOutOfRangeException(nameof(sampleRate))
            };
            if (stop <= StartBand || stop - StartBand > maximumWidth)
            {
                throw new InvalidDataException("The SBR frequency range is invalid.");
            }
            if (header.FrequencyScale == 0)
            {
                BuildLinear(stop, header.AlternateScale);
            }
            else
            {
                BuildLogarithmic(stop, header.FrequencyScale, header.AlternateScale);
            }
            if (header.CrossoverBand >= MasterCount)
            {
                throw new InvalidDataException("The SBR crossover exceeds the master frequency table.");
            }
            HighCount = MasterCount - header.CrossoverBand;
            master.AsSpan(header.CrossoverBand, HighCount + 1).CopyTo(high);
            if (CrossoverBand > 32)
            {
                throw new InvalidDataException("The SBR crossover exceeds the analysis filterbank.");
            }
            LowCount = (HighCount + 1) / 2;
            low[0] = high[0];
            for (int index = 1; index <= LowCount; index++)
            {
                low[index] = high[index * 2 - (HighCount & 1)];
            }
            NoiseCount = Math.Max(1, Round(header.NoiseBands * Math.Log2((double)stop / CrossoverBand)));
            if (NoiseCount > 5 || NoiseCount > LowCount)
            {
                throw new InvalidDataException("The SBR noise frequency table is invalid.");
            }
            noise[0] = low[0];
            int band = 0;
            for (int index = 1; index <= NoiseCount; index++)
            {
                band += (LowCount - band) / (NoiseCount + 1 - index);
                noise[index] = low[band];
            }
        }

        private void BuildLinear(int stop, bool alternate)
        {
            int width = alternate ? 2 : 1;
            int count = alternate ? ((stop - StartBand + 2) / 4) * 2 : ((stop - StartBand) / 2) * 2;
            if (count == 0)
            {
                throw new InvalidDataException("The SBR master frequency table is empty.");
            }
            Span<int> widths = stackalloc int[64];
            widths[..count].Fill(width);
            int difference = stop - StartBand - count * width;
            int direction = Math.Sign(difference);
            int index = direction > 0 ? count - 1 : 0;
            while (difference != 0)
            {
                widths[index] += direction;
                index -= direction;
                difference -= direction;
            }
            MasterCount = count;
            Accumulate(widths[..count], StartBand, master);
        }

        private void BuildLogarithmic(int stop, int scale, bool alternate)
        {
            int bands = scale switch
            {
                1 => 12,
                2 => 10,
                3 => 8,
                _ => throw new InvalidDataException("The SBR frequency scale is invalid.")
            };
            bool twoRegions = (double)stop / StartBand > 2.2449;
            int middle = twoRegions ? StartBand * 2 : stop;
            int firstCount = 2 * Round(0.5 * bands * Math.Log2((double)middle / StartBand));
            Span<int> first = stackalloc int[64];
            BuildWidths(StartBand, middle, firstCount, first);
            Accumulate(first[..firstCount], StartBand, master);
            MasterCount = firstCount;
            if (!twoRegions)
            {
                return;
            }
            double warp = alternate ? 1.3 : 1;
            int secondCount = 2 * Round(0.5 * bands * Math.Log2((double)stop / middle) / warp);
            Span<int> second = stackalloc int[64];
            BuildWidths(middle, stop, secondCount, second);
            if (first[firstCount - 1] > second[0])
            {
                int change = Math.Min(first[firstCount - 1] - second[0], (second[secondCount - 1] - second[0]) / 2);
                second[0] += change;
                second[secondCount - 1] -= change;
                second[..secondCount].Sort();
            }
            if (firstCount + secondCount > 64)
            {
                throw new InvalidDataException("The SBR master frequency table is too large.");
            }
            Accumulate(second[..secondCount], middle, master.AsSpan(firstCount));
            MasterCount += secondCount;
        }

        private static void BuildWidths(int start, int stop, int count, Span<int> widths)
        {
            if (count is < 1 or > 64)
            {
                throw new InvalidDataException("The SBR master frequency band count is invalid.");
            }
            int previous = start;
            for (int index = 1; index <= count; index++)
            {
                int border = Round(start * Math.Pow((double)stop / start, (double)index / count));
                widths[index - 1] = border - previous;
                previous = border;
            }
            widths[..count].Sort();
            if (widths[0] <= 0)
            {
                throw new InvalidDataException("The SBR master frequency bands overlap.");
            }
        }

        private static void Accumulate(ReadOnlySpan<int> widths, int start, Span<int> destination)
        {
            destination[0] = start;
            for (int index = 0; index < widths.Length; index++)
            {
                destination[index + 1] = destination[index] + widths[index];
            }
        }

        private static int GetStartBand(int frequency, int rate)
        {
            ReadOnlySpan<int> offsets = rate switch
            {
                16000 => [-8, -7, -6, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7],
                22050 => [-5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13],
                24000 => [-5, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13, 16],
                32000 => [-6, -4, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13, 16],
                44100 or 48000 or 64000 => [-4, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13, 16, 20],
                88200 or 96000 => [-2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 9, 11, 13, 16, 20, 24],
                _ => throw new NotSupportedException("The SBR output sampling frequency is not supported.")
            };
            int minimum = rate switch
            {
                16000 => 24,
                22050 => 17,
                24000 or 32000 => 16,
                44100 => 12,
                48000 => 11,
                64000 => 10,
                88200 or 96000 => 7,
                _ => throw new NotSupportedException("The SBR output sampling frequency is not supported.")
            };
            return minimum + offsets[frequency];
        }

        private static int GetStopBand(int frequency, int rate, int start)
        {
            if (frequency is 14 or 15)
            {
                return Math.Min(64, start * (frequency - 12));
            }
            int minimum = rate switch
            {
                16000 => 48,
                22050 => 35,
                24000 or 32000 => 32,
                44100 => 23,
                48000 => 21,
                64000 => 20,
                88200 => 15,
                96000 => 13,
                _ => throw new NotSupportedException("The SBR output sampling frequency is not supported.")
            };
            Span<int> widths = stackalloc int[13];
            BuildWidths(minimum, 64, 13, widths);
            int stop = minimum;
            for (int index = 0; index < frequency; index++)
            {
                stop += widths[index];
            }
            return stop;
        }

        private static int Round(double value) => (int)Math.Floor(value + 0.5);
    }
}
