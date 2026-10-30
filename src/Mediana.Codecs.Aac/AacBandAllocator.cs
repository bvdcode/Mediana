// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal ref struct AacBandAllocator
    {
        private const int CandidateSteps = 4;
        private const int MaximumMoves = 32;
        private const int PairCandidates = 4;
        private readonly ReadOnlySpan<double> spectrum;
        private readonly ReadOnlySpan<double> powered;
        private readonly ReadOnlySpan<int> boundaries;
        private readonly Span<int> scratch;
        private readonly Span<AacBandQuantization> bands;
        private readonly Span<AacBandQuantization> candidates;

        public AacBandAllocator(ReadOnlySpan<double> spectrum, ReadOnlySpan<double> powered, ReadOnlySpan<int> boundaries,
            Span<int> scratch, Span<AacBandQuantization> bands, Span<AacBandQuantization> candidates)
        {
            this.spectrum = spectrum;
            this.powered = powered;
            this.boundaries = boundaries;
            this.scratch = scratch;
            this.bands = bands;
            this.candidates = candidates;
        }

        public void Allocate(int minimumScale, int budget, CancellationToken token)
        {
            int low = minimumScale;
            int high = AacQuantizer.MaximumScale;
            while (low < high)
            {
                token.ThrowIfCancellationRequested();
                int middle = (low + high) / 2;
                Fill(middle);
                if (AacChannelEncoder.Count(bands) <= budget)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            Fill(low);
            if (AacChannelEncoder.Count(bands) > budget)
            {
                throw new NotSupportedException("The PCM frame cannot fit the configured AAC bit budget.");
            }
            for (int band = 0; band < bands.Length; band++)
            {
                token.ThrowIfCancellationRequested();
                UpdateCandidates(band);
            }
            Span<int> refinements = stackalloc int[PairCandidates];
            Span<int> savings = stackalloc int[PairCandidates];
            Span<double> refineScores = stackalloc double[PairCandidates];
            Span<double> savingScores = stackalloc double[PairCandidates];
            for (int move = 0; move < MaximumMoves; move++)
            {
                token.ThrowIfCancellationRequested();
                refinements.Fill(-1);
                savings.Fill(-1);
                refineScores.Fill(double.NegativeInfinity);
                savingScores.Fill(double.NegativeInfinity);
                int currentBits = AacChannelEncoder.Count(bands);
                int first = -1;
                int second = -1;
                double best = 0;
                for (int index = 0; index < candidates.Length; index++)
                {
                    AacBandQuantization candidate = candidates[index];
                    if (!double.IsFinite(candidate.Error))
                    {
                        continue;
                    }
                    int band = index / 2;
                    AacBandQuantization saved = bands[band];
                    bands[band] = candidate;
                    int count = AacChannelEncoder.Count(bands);
                    bands[band] = saved;
                    if (count == int.MaxValue)
                    {
                        continue;
                    }
                    int extra = count - currentBits;
                    double improvement = saved.Error - candidate.Error;
                    if (improvement > 0)
                    {
                        double score = improvement / Math.Max(1, extra);
                        if (count <= budget && score > best)
                        {
                            best = score;
                            first = index;
                        }
                        if (extra > 0)
                        {
                            Insert(refinements, refineScores, index, score);
                        }
                    }
                    else if (extra < 0)
                    {
                        Insert(savings, savingScores, index, improvement / -extra);
                    }
                }
                if (first < 0)
                {
                    double improvement = 0;
                    foreach (int refinement in refinements)
                    {
                        if (refinement < 0)
                        {
                            continue;
                        }
                        foreach (int saving in savings)
                        {
                            if (saving < 0 || refinement / 2 == saving / 2)
                            {
                                continue;
                            }
                            int firstBand = refinement / 2;
                            int secondBand = saving / 2;
                            AacBandQuantization savedFirst = bands[firstBand];
                            AacBandQuantization savedSecond = bands[secondBand];
                            double reduction = savedFirst.Error + savedSecond.Error - candidates[refinement].Error - candidates[saving].Error;
                            if (reduction <= improvement)
                            {
                                continue;
                            }
                            bands[firstBand] = candidates[refinement];
                            bands[secondBand] = candidates[saving];
                            int count = AacChannelEncoder.Count(bands);
                            bands[firstBand] = savedFirst;
                            bands[secondBand] = savedSecond;
                            if (count <= budget)
                            {
                                improvement = reduction;
                                first = refinement;
                                second = saving;
                            }
                        }
                    }
                }
                if (first < 0)
                {
                    return;
                }
                bands[first / 2] = candidates[first];
                if (second >= 0)
                {
                    bands[second / 2] = candidates[second];
                }
                UpdateCandidates(first / 2);
                if (second >= 0)
                {
                    UpdateCandidates(second / 2);
                }
            }
        }

        private void Fill(int scale)
        {
            for (int band = 0; band < bands.Length; band++)
            {
                bands[band] = Measure(band, scale);
            }
        }

        private AacBandQuantization Measure(int band, int scale)
        {
            int start = boundaries[band];
            int count = boundaries[band + 1] - start;
            return AacBandQuantization.Measure(spectrum.Slice(start, count), powered.Slice(start, count), scratch[..count], scale);
        }

        private void UpdateCandidates(int band)
        {
            AacBandQuantization current = bands[band];
            for (int direction = 0; direction < 2; direction++)
            {
                AacBandQuantization best = new(current.Scale, 0, 0, double.PositiveInfinity);
                double score = double.NegativeInfinity;
                for (int step = 1; step <= CandidateSteps; step++)
                {
                    int scale = current.Scale + (direction == 0 ? -step : step);
                    if (scale is < 0 or > AacQuantizer.MaximumScale)
                    {
                        break;
                    }
                    AacBandQuantization candidate = Measure(band, scale);
                    if (!double.IsFinite(candidate.Error))
                    {
                        continue;
                    }
                    double improvement = current.Error - candidate.Error;
                    int extra = candidate.Bits - current.Bits;
                    double merit;
                    if (improvement > 0)
                    {
                        merit = improvement / Math.Max(1, extra);
                    }
                    else if (extra < 0)
                    {
                        merit = improvement / -extra;
                    }
                    else
                    {
                        continue;
                    }
                    if (merit > score)
                    {
                        best = candidate;
                        score = merit;
                    }
                }
                candidates[band * 2 + direction] = best;
            }
        }

        private static void Insert(Span<int> indices, Span<double> scores, int index, double score)
        {
            for (int position = 0; position < indices.Length; position++)
            {
                if (score > scores[position])
                {
                    for (int next = indices.Length - 1; next > position; next--)
                    {
                        indices[next] = indices[next - 1];
                        scores[next] = scores[next - 1];
                    }
                    indices[position] = index;
                    scores[position] = score;
                    return;
                }
            }
        }
    }
}
