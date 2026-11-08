// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacSbrPatches
    {
        private readonly int[] sourceBands = new int[64];
        private readonly int[] patchBorders = new int[7];
        public int Count { get; private set; }
        public ReadOnlySpan<int> SourceBands => sourceBands;
        public ReadOnlySpan<int> Borders => patchBorders.AsSpan(0, Count + 1);

        public void Update(AacSbrFrequencyTables tables, int sampleRate)
        {
            Array.Fill(sourceBands, -1);
            Count = 0;
            int goal = (int)Math.Floor(2048000.0 / sampleRate + 0.5);
            int masterIndex = tables.MasterCount;
            if (goal < tables.StopBand)
            {
                masterIndex = 0;
                while (tables.Master[masterIndex] < goal)
                {
                    masterIndex++;
                }
            }
            int sourceLimit = tables.StartBand;
            int target = tables.CrossoverBand;
            int iterations = 0;
            while (target != tables.StopBand)
            {
                if (++iterations > 12)
                {
                    throw new InvalidDataException("The SBR patch construction cannot reach its stop frequency.");
                }
                int index = masterIndex;
                int end;
                int parity;
                do
                {
                    if (index < 0)
                    {
                        throw new InvalidDataException("The SBR patch has no valid source bands.");
                    }
                    end = tables.Master[index--];
                    parity = (end - 2 + tables.StartBand) & 1;
                }
                while (end > tables.StartBand - 1 + sourceLimit - parity);
                int width = Math.Max(0, end - target);
                if (width > 0)
                {
                    if (Count == 6)
                    {
                        throw new InvalidDataException("The SBR patch count is invalid.");
                    }
                    int source = tables.StartBand - parity - width;
                    if (source < 0 || source + width > tables.StartBand)
                    {
                        throw new InvalidDataException("The SBR patch source is outside the low frequency spectrum.");
                    }
                    patchBorders[Count++] = target;
                    for (int band = 0; band < width; band++)
                    {
                        sourceBands[target + band] = source + band;
                    }
                    target = sourceLimit = end;
                }
                else
                {
                    sourceLimit = tables.CrossoverBand;
                }
                if (tables.Master[masterIndex] - end < 3)
                {
                    masterIndex = tables.MasterCount;
                }
            }
            if (Count > 1 && target - patchBorders[Count - 1] < 3)
            {
                sourceBands.AsSpan(patchBorders[--Count], target - patchBorders[Count]).Fill(-1);
            }
            if (Count > 5)
            {
                throw new InvalidDataException("The SBR patch count exceeds five.");
            }
            patchBorders[Count] = tables.StopBand;
        }

        public int BuildLimiter(AacSbrFrequencyTables tables, int limiterBands, Span<int> output)
        {
            if (limiterBands == 0)
            {
                output[0] = tables.CrossoverBand;
                output[1] = tables.StopBand;
                return 1;
            }
            double bandsPerOctave = limiterBands switch
            {
                1 => 1.2,
                2 => 2,
                3 => 3,
                _ => throw new InvalidDataException("The SBR limiter band setting is invalid.")
            };
            int count = tables.Low.Length;
            tables.Low.CopyTo(output);
            for (int index = 1; index < Count; index++)
            {
                output[count++] = patchBorders[index];
            }
            output[..count].Sort();
            int previous = 0;
            int current = 1;
            while (current < count)
            {
                bool close = bandsPerOctave * Math.Log2((double)output[current] / output[previous]) < 0.49;
                if (close && (output[current] == output[previous] || !Borders.Contains(output[current])))
                {
                    output[(current + 1)..count].CopyTo(output[current..]);
                    count--;
                }
                else if (close && !Borders.Contains(output[previous]))
                {
                    output[current..count].CopyTo(output[previous..]);
                    count--;
                }
                else
                {
                    previous = current++;
                }
            }
            return count - 1;
        }
    }
}
