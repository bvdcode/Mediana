// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class ChromaUpsamplingAxis
    {
        private readonly int[] starts;
        private readonly float[] weights;
        private readonly int phaseMask;

        public ChromaUpsamplingAxis(int sourceLength, int factor, int offset, ChromaUpsamplingFilter filter)
        {
            SourceLength = sourceLength;
            Length = checked(sourceLength * factor);
            Taps = factor == 1 ? 1 : ReconstructionKernel.Taps(filter);
            starts = new int[Length];
            phaseMask = factor - 1;
            weights = new float[factor * Taps];
            for (int x = 0; x < Length; x++)
            {
                double position = factor == 1 ? x : (x - offset / 2d) / factor;
                starts[x] = FirstSample(position);
            }
            for (int phase = 0; phase < factor; phase++)
            {
                double position = factor == 1 ? phase : (phase - offset / 2d) / factor;
                int start = FirstSample(position);
                double total = 0;
                for (int tap = 0; tap < Taps; tap++)
                {
                    double weight = Taps == 1 ? 1 : ReconstructionKernel.Weight(filter, position - start - tap);
                    weights[phase * Taps + tap] = (float)weight;
                    total += weight;
                }
                for (int tap = 0; tap < Taps; tap++)
                {
                    weights[phase * Taps + tap] /= (float)total;
                }
            }
        }

        public int SourceLength { get; }
        public int Length { get; }
        public int Taps { get; }
        public int First(int x) => starts[x];
        public ReadOnlySpan<float> Weights(int x) => weights.AsSpan((x & phaseMask) * Taps, Taps);

        private int FirstSample(double position) => Taps switch
        {
            1 => (int)Math.Ceiling(position - 0.5),
            2 or 4 or 6 => (int)Math.Floor(position) - Taps / 2 + 1,
            _ => throw new InvalidOperationException("Invalid reconstruction kernel width.")
        };
    }
}
