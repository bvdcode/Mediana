// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class ChromaUpsamplingAxis
    {
        private readonly int[] starts;
        private readonly float[] weights;

        public ChromaUpsamplingAxis(int sourceLength, int factor, int offset, ChromaUpsamplingFilter filter)
        {
            SourceLength = sourceLength;
            Length = checked(sourceLength * factor);
            Taps = factor == 1 ? 1 : ReconstructionKernel.Taps(filter);
            starts = new int[Length];
            weights = new float[checked(Length * Taps)];
            for (int x = 0; x < Length; x++)
            {
                double position = factor == 1 ? x : (x - offset / 2d) / factor;
                int start = Taps == 1 ? (int)Math.Ceiling(position - 0.5) : (int)Math.Floor(position) - Taps / 2 + 1;
                starts[x] = start;
                double total = 0;
                for (int tap = 0; tap < Taps; tap++)
                {
                    double weight = Taps == 1 ? 1 : ReconstructionKernel.Weight(filter, position - start - tap);
                    weights[x * Taps + tap] = (float)weight;
                    total += weight;
                }
                for (int tap = 0; tap < Taps; tap++)
                {
                    weights[x * Taps + tap] /= (float)total;
                }
            }
        }

        public int SourceLength { get; }
        public int Length { get; }
        public int Taps { get; }
        public int First(int x) => starts[x];
        public ReadOnlySpan<float> Weights(int x) => weights.AsSpan(x * Taps, Taps);
    }
}
