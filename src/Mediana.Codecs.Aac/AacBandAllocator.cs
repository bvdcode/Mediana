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
        private readonly ReadOnlySpan<double> energy;
        private readonly ReadOnlySpan<int> boundaries;
        private readonly int bandsPerGroup;
        private readonly int sectionBits;
        private readonly Span<int> scratch;
        private readonly Span<AacBandQuantization> bands;
        private readonly Span<AacBandQuantization> candidates;

        public AacBandAllocator(ReadOnlySpan<double> spectrum, ReadOnlySpan<double> powered, ReadOnlySpan<double> energy, AacEncodingWindow window,
            Span<int> scratch, Span<AacBandQuantization> bands, Span<AacBandQuantization> candidates)
        {
            this.spectrum = spectrum;
            this.powered = powered;
            this.energy = energy;
            boundaries = window.Boundaries;
            bandsPerGroup = window.BandsPerGroup;
            sectionBits = window.SectionLengthBits;
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
                if (Count() <= budget)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            Fill(low);
            if (Count() > budget)
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
            int channelBands = boundaries.Length - 1;
            int channels = bands.Length / channelBands;
            Span<int> channelBits = stackalloc int[channels];
            int currentBits = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                channelBits[channel] = AacChannelEncoder.Count(bands.Slice(channel * channelBands, channelBands), bandsPerGroup, sectionBits);
                currentBits += channelBits[channel];
            }
            Span<int> candidateBits = stackalloc int[candidates.Length];
            candidateBits.Fill(-1);
            bool includePlateaus = false;
            for (int move = 0; move < MaximumMoves * channels; move++)
            {
                token.ThrowIfCancellationRequested();
                refinements.Fill(-1);
                savings.Fill(-1);
                refineScores.Fill(double.NegativeInfinity);
                savingScores.Fill(double.NegativeInfinity);
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
                    int channel = band / channelBands;
                    int count = candidateBits[index];
                    if (count < 0)
                    {
                        bands[band] = candidate;
                        count = AacChannelEncoder.Count(bands.Slice(channel * channelBands, channelBands), bandsPerGroup, sectionBits);
                        bands[band] = saved;
                        candidateBits[index] = count;
                    }
                    if (count == int.MaxValue)
                    {
                        continue;
                    }
                    count += currentBits - channelBits[channel];
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
                            int count = Estimate(channelBits, currentBits, firstBand, secondBand);
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
                    if (!includePlateaus)
                    {
                        includePlateaus = true;
                        for (int band = 0; band < bands.Length; band++)
                        {
                            if (bands[band].Book == 0)
                            {
                                UpdateCandidates(band, includePlateaus);
                                candidateBits[band * 2] = -1;
                                candidateBits[band * 2 + 1] = -1;
                            }
                        }
                        continue;
                    }
                    return;
                }
                bands[first / 2] = candidates[first];
                if (second >= 0)
                {
                    bands[second / 2] = candidates[second];
                }
                UpdateCandidates(first / 2, includePlateaus);
                InvalidateChannelCosts(first / 2, candidateBits);
                UpdateChannelBits(first / 2, channelBits, ref currentBits);
                if (second >= 0)
                {
                    UpdateCandidates(second / 2, includePlateaus);
                    InvalidateChannelCosts(second / 2, candidateBits);
                    if (second / 2 / channelBands != first / 2 / channelBands)
                    {
                        UpdateChannelBits(second / 2, channelBits, ref currentBits);
                    }
                }
            }
        }

        private void UpdateChannelBits(int band, scoped Span<int> channelBits, ref int total)
        {
            int channelBands = boundaries.Length - 1;
            int channel = band / channelBands;
            int count = AacChannelEncoder.Count(bands.Slice(channel * channelBands, channelBands), bandsPerGroup, sectionBits);
            total += count - channelBits[channel];
            channelBits[channel] = count;
        }

        private void InvalidateChannelCosts(int band, scoped Span<int> candidateBits)
        {
            int channelBands = boundaries.Length - 1;
            int channel = band / channelBands;
            candidateBits.Slice(channel * channelBands * 2, channelBands * 2).Fill(-1);
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
            int channelBands = boundaries.Length - 1;
            int localBand = band % channelBands;
            int start = band / channelBands * boundaries[^1] + boundaries[localBand];
            int count = boundaries[localBand + 1] - boundaries[localBand];
            AacBandQuantization measured = AacBandQuantization.Measure(spectrum.Slice(start, count), powered.Slice(start, count), scratch[..count], scale);
            double channelEnergy = energy[band / channelBands];
            if (energy.Length == 1 || channelEnergy == 0 || !double.IsFinite(measured.Error))
            {
                return measured;
            }
            return measured with { Error = measured.Error / channelEnergy };
        }

        private int Count()
        {
            int channelBands = boundaries.Length - 1;
            int total = 0;
            for (int offset = 0; offset < bands.Length; offset += channelBands)
            {
                int count = AacChannelEncoder.Count(bands.Slice(offset, channelBands), bandsPerGroup, sectionBits);
                if (count == int.MaxValue)
                {
                    return int.MaxValue;
                }
                total += count;
            }
            return total;
        }

        private int Estimate(scoped ReadOnlySpan<int> channelBits, int total, int firstBand, int secondBand = -1)
        {
            int channelBands = boundaries.Length - 1;
            int firstChannel = firstBand / channelBands;
            int first = AacChannelEncoder.Count(bands.Slice(firstChannel * channelBands, channelBands), bandsPerGroup, sectionBits);
            if (first == int.MaxValue)
            {
                return int.MaxValue;
            }
            int result = total - channelBits[firstChannel] + first;
            if (secondBand >= 0)
            {
                int secondChannel = secondBand / channelBands;
                if (secondChannel != firstChannel)
                {
                    int second = AacChannelEncoder.Count(bands.Slice(secondChannel * channelBands, channelBands), bandsPerGroup, sectionBits);
                    if (second == int.MaxValue)
                    {
                        return int.MaxValue;
                    }
                    result += second - channelBits[secondChannel];
                }
            }
            return result;
        }

        private void UpdateCandidates(int band, bool includePlateaus = false)
        {
            AacBandQuantization current = bands[band];
            int refinement = current.Scale - 1;
            int directions = 2;
            if (current.Book == 0 && includePlateaus)
            {
                int channelBands = boundaries.Length - 1;
                int local = band % channelBands;
                int start = band / channelBands * boundaries[^1] + boundaries[local];
                refinement = AacQuantizer.CoarsestNonzeroScale(spectrum.Slice(start, boundaries[local + 1] - boundaries[local]), refinement);
                directions = 1;
                candidates[band * 2 + 1] = new(current.Scale, 0, 0, double.PositiveInfinity);
            }
            for (int direction = 0; direction < directions; direction++)
            {
                AacBandQuantization best = new(current.Scale, 0, 0, double.PositiveInfinity);
                double score = double.NegativeInfinity;
                for (int step = 1; step <= CandidateSteps; step++)
                {
                    int scale = direction == 0 ? refinement - step + 1 : current.Scale + step;
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
