// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.H264
{
    internal ref partial struct H264MotionSearch
    {
        private const int LumaFirstMinimumQuantizer = 18;
        private const int LumaFirstMaximumQuantizer = 33;
        private const int LumaFirstMaximumChromaDifference = 96;

        private readonly H264Picture reference;
        private readonly (int X, int Y) position;
        private readonly (int X, int Y) predicted;
        private readonly ReadOnlySpan<byte> source;
        private readonly Span<byte> prediction;
        private readonly H264MotionReference? interpolation;
        private readonly int motionBitWeight;
        private readonly int initialLumaDifference;
        private bool deferChroma;
        private int bestCost;
        private int bestVectorBits;
        private int best;
        private int motionX;
        private int motionY;

        private H264MotionSearch(H264Picture reference, (int X, int Y) position, (int X, int Y) predicted,
            ReadOnlySpan<byte> source, Span<byte> prediction, H264MotionReference? interpolation, int motionBitWeight)
        {
            this.reference = reference;
            this.position = position;
            this.predicted = predicted;
            this.source = source;
            this.prediction = prediction;
            this.interpolation = interpolation;
            this.motionBitWeight = motionBitWeight;
            motionX = 0;
            motionY = 0;
            ReadOnlySpan<byte> luma = reference.ReadPlane(0)[(position.Y * reference.Width + position.X)..];
            initialLumaDifference = H264SampleDifference.Sum16x16(source[..256], luma, reference.Width, int.MaxValue);
            best = initialLumaDifference + H264ChromaDifference.Sum8x8(reference, position.X, position.Y, 0, 0, source[256..], int.MaxValue);
            bestVectorBits = VectorBits(-predicted.X, -predicted.Y);
            bestCost = best + motionBitWeight * bestVectorBits;
        }

        public static (int X, int Y, bool Exact) Select(H264SourceMacroblock input, H264Picture reference, H264Picture target,
            int x, int y, int range, Span<byte> prediction, CancellationToken token, H264MotionReference? interpolation = null) =>
            Select<H264FastMacroblockAnalysis>(input, reference, target, x, y, range, prediction, token, interpolation);

        public static (int X, int Y, bool Exact) Select<TAnalysis>(H264SourceMacroblock input, H264Picture reference, H264Picture target,
            int x, int y, int range, Span<byte> prediction, CancellationToken token, H264MotionReference? interpolation = null)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            (int motionX, int motionY, int difference) = SelectWithDifference<TAnalysis>(input, reference, target,
                x, y, range, prediction, token, interpolation);
            return (motionX, motionY, difference == 0);
        }

        public static (int X, int Y, int Difference) SelectWithDifference<TAnalysis>(H264SourceMacroblock input,
            H264Picture reference, H264Picture target, int x, int y, int range, Span<byte> prediction,
            CancellationToken token, H264MotionReference? interpolation, int quantizer = 0)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
            => SelectWithDifference<TAnalysis>(input, reference, target, x, y, range, prediction,
                token, interpolation, quantizer, out _);

        public static (int X, int Y, int Difference) SelectWithDifference<TAnalysis>(H264SourceMacroblock input,
            H264Picture reference, H264Picture target, int x, int y, int range, Span<byte> prediction,
            CancellationToken token, H264MotionReference? interpolation, int quantizer, out int estimatedVectorBits,
            bool lumaFirst = false)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            token.ThrowIfCancellationRequested();
            (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
            H264MotionReference? wideReference = range > H264MotionReference.LocalSearchRange && interpolation is { HasQuarterLuma: true }
                ? interpolation : null;
            int motionBitWeight = wideReference is not null && quantizer > 0
                ? 1 << Math.Max(0, (quantizer - 18) / 6) : 0;
            H264MotionSearch search = new(reference, (x, y), (predictedX, predictedY), input.Samples, prediction, interpolation, motionBitWeight);
            if (search.best == 0 && predictedX == 0 && predictedY == 0)
            {
                input.Samples.CopyTo(prediction);
                estimatedVectorBits = search.bestVectorBits;
                return (0, 0, 0);
            }
            // With a zero predictor the integer diamond stays at the origin on a tie.
            // A constant six-tap footprint also makes every fractional refinement identical.
            if (wideReference is null && !TAnalysis.Enabled && range != 0 && predictedX == 0 && predictedY == 0 &&
                H264ConstantRegion.TryReadExtendedBlock(reference, 0, x, y, 16, 3, out byte luma) &&
                H264ConstantRegion.TryReadExtendedBlock(reference, 1, x / 2, y / 2, 8, 1, out byte blue) &&
                H264ConstantRegion.TryReadExtendedBlock(reference, 2, x / 2, y / 2, 8, 1, out byte red))
            {
                prediction[..256].Fill(luma);
                prediction.Slice(256, 64).Fill(blue);
                prediction.Slice(320, 64).Fill(red);
                estimatedVectorBits = search.bestVectorBits;
                return (0, 0, search.best);
            }
            int initialChromaDifference = search.best - search.initialLumaDifference;
            bool lumaFirstRefinement = lumaFirst && !TAnalysis.Enabled && range != 0 &&
                quantizer is >= LumaFirstMinimumQuantizer and <= LumaFirstMaximumQuantizer &&
                initialChromaDifference <= LumaFirstMaximumChromaDifference;
            if (lumaFirstRefinement)
            {
                search.deferChroma = true;
                search.best = search.initialLumaDifference;
                search.bestCost = search.best + motionBitWeight * search.bestVectorBits;
            }
            search.Evaluate(Math.Clamp(predictedX / 4, -range, range) * 4, Math.Clamp(predictedY / 4, -range, range) * 4);
            if (TAnalysis.Enabled || wideReference is not null)
            {
                search.Seed(target, range);
            }
            if (wideReference is not null && search.best >= 2 * H264SourceMacroblock.ByteCount)
            {
                H264CoarseMotionSearch coarse = new(wideReference.QuarterLuma, reference.Width / H264QuarterLuma.Scale,
                    reference.Height / H264QuarterLuma.Scale,
                    H264QuarterLuma.ReduceMacroblock(input.Samples), (x / H264QuarterLuma.Scale, y / H264QuarterLuma.Scale),
                    (predictedX, predictedY));
                (int coarseX, int coarseY) = coarse.Find(range);
                search.Evaluate(coarseX, coarseY);
            }
            search.Scan(range, token);
            (int integerX, int integerY) = (search.motionX, search.motionY);
            search.Refine<TAnalysis>(range, token, interpolation);
            if (lumaFirstRefinement)
            {
                search.Evaluate(integerX, integerY);
                search.Evaluate(0, 0);
            }
            if (search.best == 0)
            {
                input.Samples.CopyTo(prediction);
            }
            else
            {
                search.WritePrediction();
            }
            estimatedVectorBits = search.bestVectorBits;
            return (search.motionX, search.motionY, search.best);
        }

        private void Scan(int range, CancellationToken token)
        {
            int limit = range * 4;
            ReadOnlySpan<int> stepsX = [-4, 4, 0, 0];
            ReadOnlySpan<int> stepsY = [0, 0, -4, 4];
            int previousX = int.MinValue;
            int previousY = int.MinValue;
            for (int iteration = 0; iteration < range * 2; iteration++)
            {
                token.ThrowIfCancellationRequested();
                if (best == 0 && motionX == predicted.X && motionY == predicted.Y)
                {
                    break;
                }
                int centerX = motionX;
                int centerY = motionY;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nextX = centerX + stepsX[direction];
                    int nextY = centerY + stepsY[direction];
                    if (Math.Abs(nextX) <= limit && Math.Abs(nextY) <= limit && (nextX != previousX || nextY != previousY))
                    {
                        Evaluate(nextX, nextY);
                    }
                }
                if (centerX == motionX && centerY == motionY)
                {
                    break;
                }
                previousX = centerX;
                previousY = centerY;
            }
        }

        private void IncludeChroma()
        {
            if (deferChroma)
            {
                best += H264ChromaDifference.Sum8x8(reference, position.X, position.Y, motionX, motionY, source[256..], int.MaxValue);
                bestCost = best + motionBitWeight * bestVectorBits;
                deferChroma = false;
            }
        }

        private void Refine<TAnalysis>(int range, CancellationToken token, H264MotionReference? interpolation)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            if (range == 0 || (best == 0 && motionX == predicted.X && motionY == predicted.Y))
            {
                IncludeChroma();
                return;
            }
            int originX = motionX;
            int originY = motionY;
            if (interpolation is null)
            {
                RefineUncached<TAnalysis>(range, originX, originY, token);
            }
            else
            {
                H264LumaNeighborhood neighborhood = new(interpolation,
                    position.X + (originX >> 2), position.Y + (originY >> 2));
                RefineNeighborhood<TAnalysis>(neighborhood, range, originX, originY, token);
            }
            IncludeChroma();
            int limit = range * 4;
            Evaluate(Math.Clamp(predicted.X, -limit, limit), Math.Clamp(predicted.Y, -limit, limit));
        }

        private void RefineUncached<TAnalysis>(int range, int originX, int originY, CancellationToken token)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            Span<byte> samples = stackalloc byte[H264LumaNeighborhood.SampleBytes];
            Span<byte> filtered = stackalloc byte[H264LumaNeighborhood.FilteredBytes];
            H264LumaNeighborhood neighborhood = new(reference, (long)position.X + (originX >> 2),
                (long)position.Y + (originY >> 2), samples, filtered);
            RefineNeighborhood<TAnalysis>(neighborhood, range, originX, originY, token);
        }

        private void RefineNeighborhood<TAnalysis>(scoped H264LumaNeighborhood neighborhood, int range,
            int originX, int originY, CancellationToken token)
            where TAnalysis : struct, IH264MacroblockAnalysisStrategy
        {
            int limit = range * 4;
            ReadOnlySpan<int> offsetsX = TAnalysis.SubpixelOffsetsX;
            ReadOnlySpan<int> offsetsY = TAnalysis.SubpixelOffsetsY;
            for (int step = 2; step >= 1 && range > 0; step /= 2)
            {
                if (step == 1)
                {
                    IncludeChroma();
                }
                token.ThrowIfCancellationRequested();
                if (best == 0 && motionX == predicted.X && motionY == predicted.Y)
                {
                    break;
                }
                int centerX = motionX;
                int centerY = motionY;
                if (!TAnalysis.Enabled && Math.Abs(centerX) <= limit - step && Math.Abs(centerY) <= limit - step)
                {
                    CompareFastCross(neighborhood, originX, originY, centerX, centerY, step);
                    continue;
                }
                for (int direction = 0; direction < offsetsX.Length; direction++)
                {
                    int nextX = centerX + offsetsX[direction] * step;
                    int nextY = centerY + offsetsY[direction] * step;
                    if (Math.Abs(nextX) <= limit && Math.Abs(nextY) <= limit && (nextX != motionX || nextY != motionY))
                    {
                        int difference = neighborhood.Select(nextX - originX, nextY - originY).Difference(source[..256], bestCost);
                        Compare(nextX, nextY, difference);
                    }
                }
            }
        }

        private void CompareFastCross(scoped H264LumaNeighborhood neighborhood, int originX, int originY,
            int centerX, int centerY, int step)
        {
            int x = centerX - originX;
            int y = centerY - originY;
            int left = centerX - step;
            int right = centerX + step;
            int above = centerY - step;
            int below = centerY + step;

            if (step == 1 && ((x ^ y) & 2) == 0)
            {
                (int upperLeftCost, int upperCost, int upperRightCost, int westCost, int eastCost,
                    int lowerLeftCost, int lowerCost, int lowerRightCost) = neighborhood.DifferenceEight(source[..256], x, y);
                Compare(left, above, upperLeftCost);
                Compare(centerX, above, upperCost);
                Compare(right, above, upperRightCost);
                Compare(left, centerY, westCost);
                Compare(right, centerY, eastCost);
                Compare(left, below, lowerLeftCost);
                Compare(centerX, below, lowerCost);
                Compare(right, below, lowerRightCost);
                return;
            }

            Compare(left, above, neighborhood.Select(x - step, y - step).Difference(source[..256], bestCost));
            (int leftCost, int rightCost, int aboveCost, int belowCost) = neighborhood.DifferenceCross(source[..256], x, y, step, bestCost);
            Compare(centerX, above, aboveCost);
            Compare(right, above, neighborhood.Select(x + step, y - step).Difference(source[..256], bestCost));
            Compare(left, centerY, leftCost);
            Compare(right, centerY, rightCost);
            Compare(left, below, neighborhood.Select(x - step, y + step).Difference(source[..256], bestCost));
            Compare(centerX, below, belowCost);
            Compare(right, below, neighborhood.Select(x + step, y + step).Difference(source[..256], bestCost));
        }

        private void Evaluate(int nextX, int nextY)
        {
            if (nextX == motionX && nextY == motionY)
            {
                return;
            }
            long x = (long)position.X + (nextX >> 2);
            long y = (long)position.Y + (nextY >> 2);
            if (interpolation is not null)
            {
                if (((nextX | nextY) & 3) == 0)
                {
                    int start = checked(((int)y + interpolation.Range + 2) * interpolation.SampleStride + (int)x + interpolation.Range + 2);
                    Compare(nextX, nextY, H264SampleDifference.Sum16x16(source[..256], interpolation.Samples[start..], interpolation.SampleStride, bestCost));
                    return;
                }
                H264LumaNeighborhood block = new(interpolation, (int)x, (int)y);
                int difference = block.Select(nextX & 3, nextY & 3).Difference(source[..256], bestCost);
                Compare(nextX, nextY, difference);
                return;
            }
            if (((nextX | nextY) & 3) == 0 && x >= 0 && x <= reference.Width - 16 && y >= 0 && y <= reference.Height - 16)
            {
                ReadOnlySpan<byte> luma = reference.ReadPlane(0).Slice((int)y * reference.Width + (int)x, 15 * reference.Width + 16);
                Compare(nextX, nextY, H264SampleDifference.Sum16x16(source[..256], luma, reference.Width, bestCost));
                return;
            }
            EvaluateUncached(nextX, nextY);
        }

        private void EvaluateUncached(int nextX, int nextY)
        {
            Span<byte> candidate = stackalloc byte[256];
            H264PredictionBlock.Read(reference, 0, position.X, position.Y, 16, 16, nextX, nextY, candidate);
            Compare(nextX, nextY, H264SampleDifference.Sum16x16(source[..256], candidate, 16, bestCost));
        }

        private void Compare(int nextX, int nextY, int difference)
        {
            if (difference > bestCost)
            {
                return;
            }
            int vectorBits = VectorBits(nextX - predicted.X, nextY - predicted.Y);
            int vectorCost = motionBitWeight * vectorBits;
            int remaining = bestCost - vectorCost - difference;
            if (remaining < 0)
            {
                return;
            }
            if (!deferChroma)
            {
                difference += H264ChromaDifference.Sum8x8(reference, position.X, position.Y, nextX, nextY, source[256..], remaining);
            }
            int cost = difference + vectorCost;
            if (cost < bestCost || (cost == bestCost && vectorBits < bestVectorBits))
            {
                bestCost = cost;
                bestVectorBits = vectorBits;
                best = difference;
                motionX = nextX;
                motionY = nextY;
            }
        }

        private void WritePrediction()
        {
            if (interpolation is null)
            {
                Predict(reference, position.X, position.Y, motionX, motionY, prediction);
                return;
            }
            H264LumaNeighborhood neighborhood = new(interpolation, position.X + (motionX >> 2), position.Y + (motionY >> 2));
            neighborhood.Select(motionX & 3, motionY & 3).Write(prediction[..256]);
            H264PredictionBlock.Read(reference, 1, position.X, position.Y, 16, 16, motionX, motionY, prediction.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, position.X, position.Y, 16, 16, motionX, motionY, prediction.Slice(320, 64));
        }

        internal static int VectorBits(int x, int y) => SignedBits(x) + SignedBits(y);
        private static int SignedBits(int value) => BitOperations.Log2((uint)(Math.Abs(value) * 2 + (value <= 0 ? 1 : 0))) * 2 + 1;

        private static void Predict(H264Picture reference, int x, int y, int motionX, int motionY, Span<byte> prediction)
        {
            H264PredictionBlock.Read(reference, 0, x, y, 16, 16, motionX, motionY, prediction[..256]);
            H264PredictionBlock.Read(reference, 1, x, y, 16, 16, motionX, motionY, prediction.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, x, y, 16, 16, motionX, motionY, prediction.Slice(320, 64));
        }
    }
}
