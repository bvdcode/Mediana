// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal struct AacTransientDetector
    {
        private const double MinimumPeak = 1500;
        private const double FineThreshold = 0.07;
        private const double CoarseThreshold = 0.4;
        private static readonly double FilterGain = 1 / (1 + Math.Tan(Math.PI / 6));
        private double previousInput;
        private double previousOutput;
        private double previousFine;
        private double previousMedium;
        private double previousCoarse;

        public static bool AnalyzeWindow(ReadOnlySpan<double> samples, CancellationToken token)
        {
            if (samples.Length != 2048)
            {
                throw new ArgumentException("AAC bounded transient analysis requires the previous and current PCM blocks.", nameof(samples));
            }
            AacTransientDetector detector = default;
            detector.Analyze(samples[..1024], token);
            return detector.Analyze(samples[1024..], token);
        }

        public bool Analyze(ReadOnlySpan<double> samples, CancellationToken token)
        {
            if (samples.Length != 1024)
            {
                throw new ArgumentException("AAC transient analysis requires one complete PCM block.", nameof(samples));
            }
            token.ThrowIfCancellationRequested();
            bool transient = false;
            double medium = 0;
            double coarse = 0;
            for (int block = 0; block < 8; block++)
            {
                token.ThrowIfCancellationRequested();
                double fine = 0;
                foreach (double sample in samples.Slice(block * 128, 128))
                {
                    double filtered = FilterGain * (sample - previousInput) + (2 * FilterGain - 1) * previousOutput;
                    previousInput = sample;
                    previousOutput = filtered;
                    fine = Math.Max(fine, Math.Abs(filtered));
                }
                transient |= Exceeds(fine, previousFine, FineThreshold);
                previousFine = fine;
                medium = Math.Max(medium, fine);
                coarse = Math.Max(coarse, fine);
                if ((block & 1) == 1)
                {
                    transient |= Exceeds(medium, previousMedium, CoarseThreshold);
                    previousMedium = medium;
                    medium = 0;
                }
                if ((block & 3) == 3)
                {
                    transient |= Exceeds(coarse, previousCoarse, CoarseThreshold);
                    previousCoarse = coarse;
                    coarse = 0;
                }
            }
            return transient;
        }

        private static bool Exceeds(double current, double previous, double threshold) => current > MinimumPeak && current * threshold > previous;
    }
}
