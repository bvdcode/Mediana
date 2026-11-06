// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltSpectralFrameReader
    {
        public static void Read(ref OpusRangeDecoder decoder, CeltPulseCodebook codebook, CeltBandMode mode,
            CeltFrameSyntax syntax, scoped ReadOnlySpan<int> resolutions, scoped ReadOnlySpan<int> shapeEighths,
            scoped CeltSpectralBuffers buffers, ref uint seed, bool disableInversion = false)
        {
            ArgumentNullException.ThrowIfNull(codebook);
            if (mode.Channels is not (1 or 2)) { throw new ArgumentException("The CELT band mode is uninitialized.", nameof(mode)); }
            if (mode.FirstBand is not (0 or CeltBandTables.HybridFirstBand))
            {
                throw new ArgumentException("Opus CELT starts at the first band or the hybrid band boundary.", nameof(mode));
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(resolutions.Length, mode.EndBand, nameof(resolutions));
            ArgumentOutOfRangeException.ThrowIfLessThan(shapeEighths.Length, mode.EndBand, nameof(shapeEighths));
            int multiplier = 1 << mode.FrameShift;
            int samples = CeltBandTables.ShortTransformSize * multiplier;
            int normOffset = CeltBandTables.Boundaries[mode.FirstBand] * multiplier;
            int normStride = CeltBandTables.Boundaries[CeltBandTables.BandCount - 1] * multiplier - normOffset;
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Coefficients.Length, mode.Channels * samples, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.Folding.Length, mode.Channels * normStride, nameof(buffers));
            ArgumentOutOfRangeException.ThrowIfLessThan(buffers.CollapseMasks.Length, mode.Channels * CeltBandTables.BandCount, nameof(buffers));
            scoped Span<float> left = buffers.Coefficients[..samples], right = default;
            scoped Span<float> leftNorm = buffers.Folding[..normStride], rightNorm = default;
            if (mode.Channels == 2)
            {
                right = buffers.Coefficients.Slice(samples, samples);
                rightNorm = buffers.Folding.Slice(normStride, normStride);
            }
            buffers.CollapseMasks[..(mode.Channels * CeltBandTables.BandCount)].Clear();
            for (int channel = 0; channel < mode.Channels; channel++)
            {
                Span<float> coefficients = buffers.Coefficients.Slice(channel * samples, samples);
                coefficients[..normOffset].Clear();
                coefficients[(CeltBandTables.Boundaries[mode.EndBand] * multiplier)..].Clear();
            }
            bool dualStereo = syntax.Allocation.DualStereo, updateFold = true;
            int foldOffset = 0, balance = syntax.Allocation.BalanceEighthBits;
            int blocks = 1;
            if (syntax.Flags.IsTransient) { blocks = multiplier; }
            int total = decoder.FrameBitCount * 8 - syntax.Controls.AntiCollapseEighthBits;
            CeltBandDecoderState state = new(codebook, mode.FirstBand, syntax.Controls.Spreading, 0, seed,
                syntax.Allocation.IntensityBand, disableInversion);
            for (int band = mode.FirstBand; band < mode.EndBand; band++)
            {
                int at = CeltBandTables.Boundaries[band] * multiplier;
                int width = CeltBandTables.Width(band) * multiplier;
                int tell = decoder.EighthBitsUsed;
                if (band != mode.FirstBand) { balance -= tell; }
                state.Band = band;
                state.RemainingEighths = total - tell - 1;
                int budget = 0;
                if (band < syntax.Allocation.CodedBands)
                {
                    int carried = balance / Math.Min(3, syntax.Allocation.CodedBands - band);
                    budget = Math.Clamp(Math.Min(state.RemainingEighths + 1, shapeEighths[band] + carried), 0, 16383);
                }
                if ((at - width >= normOffset || band == mode.FirstBand + 1) && (updateFold || foldOffset == 0))
                {
                    foldOffset = band;
                }
                if (band == mode.FirstBand + 1)
                {
                    ExtendHybridFolding(leftNorm, mode.FirstBand, multiplier);
                    if (dualStereo) { ExtendHybridFolding(rightNorm, mode.FirstBand, multiplier); }
                }
                int effectiveFold = -1;
                uint leftFill = (1u << blocks) - 1, rightFill = leftFill;
                if (foldOffset != 0 && (state.Spreading != CeltSpreading.Aggressive || blocks > 1 || resolutions[band] < 0))
                {
                    effectiveFold = Math.Max(0, CeltBandTables.Boundaries[foldOffset] * multiplier - normOffset - width);
                    int firstFold = foldOffset;
                    do { firstFold--; } while (CeltBandTables.Boundaries[firstFold] * multiplier > effectiveFold + normOffset);
                    int endFold = foldOffset - 1;
                    do { endFold++; }
                    while (endFold < band && CeltBandTables.Boundaries[endFold] * multiplier < effectiveFold + normOffset + width);
                    leftFill = rightFill = 0;
                    for (int folded = firstFold; folded < endFold; folded++)
                    {
                        leftFill |= buffers.CollapseMasks[folded * mode.Channels];
                        rightFill |= buffers.CollapseMasks[folded * mode.Channels + mode.Channels - 1];
                    }
                }
                if (dualStereo && band == syntax.Allocation.IntensityBand)
                {
                    dualStereo = false;
                    for (int folded = 0; folded < at - normOffset; folded++)
                    {
                        leftNorm[folded] = .5f * (leftNorm[folded] + rightNorm[folded]);
                    }
                }
                scoped ReadOnlySpan<float> leftFold = default, rightFold = default;
                scoped Span<float> leftOutput = default, rightOutput = default;
                if (effectiveFold >= 0)
                {
                    leftFold = leftNorm.Slice(effectiveFold, width);
                    if (dualStereo) { rightFold = rightNorm.Slice(effectiveFold, width); }
                }
                if (band < mode.EndBand - 1)
                {
                    leftOutput = leftNorm.Slice(at - normOffset, width);
                    if (dualStereo) { rightOutput = rightNorm.Slice(at - normOffset, width); }
                }
                CeltBandResolution resolution = new(mode.FrameShift, blocks, resolutions[band]);
                if (dualStereo)
                {
                    leftFill = CeltBandReader.Read(ref decoder, ref state, left.Slice(at, width), budget / 2, resolution,
                        leftFold, leftOutput, 1, leftFill);
                    rightFill = CeltBandReader.Read(ref decoder, ref state, right.Slice(at, width), budget / 2, resolution,
                        rightFold, rightOutput, 1, rightFill);
                }
                else if (mode.Channels == 2)
                {
                    leftFill = CeltStereoBandReader.Read(ref decoder, ref state, left.Slice(at, width), right.Slice(at, width), budget,
                        resolution, leftFold, leftOutput, leftFill | rightFill);
                    rightFill = leftFill;
                }
                else
                {
                    leftFill = CeltBandReader.Read(ref decoder, ref state, left.Slice(at, width), budget, resolution,
                        leftFold, leftOutput, 1, leftFill | rightFill);
                    rightFill = leftFill;
                }
                buffers.CollapseMasks[band * mode.Channels] = (byte)leftFill;
                buffers.CollapseMasks[band * mode.Channels + mode.Channels - 1] = (byte)rightFill;
                balance += shapeEighths[band] + tell;
                updateFold = budget > width * 8;
            }
            seed = state.Seed;
        }

        private static void ExtendHybridFolding(Span<float> norm, int firstBand, int multiplier)
        {
            int firstWidth = CeltBandTables.Width(firstBand) * multiplier;
            int nextWidth = CeltBandTables.Width(firstBand + 1) * multiplier;
            norm.Slice(2 * firstWidth - nextWidth, nextWidth - firstWidth).CopyTo(norm[firstWidth..]);
        }
    }
}
