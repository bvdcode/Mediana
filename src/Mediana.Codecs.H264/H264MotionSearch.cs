// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal ref struct H264MotionSearch
    {
        private readonly H264Picture reference;
        private readonly (int X, int Y) position;
        private readonly (int X, int Y) predicted;
        private readonly ReadOnlySpan<byte> source;
        private readonly Span<byte> candidate;
        private readonly Span<byte> prediction;
        private readonly H264MotionReference? interpolation;
        private int best;
        private int motionX;
        private int motionY;

        private H264MotionSearch(H264Picture reference, (int X, int Y) position, (int X, int Y) predicted,
            ReadOnlySpan<byte> source, Span<byte> candidate, Span<byte> prediction, H264MotionReference? interpolation)
        {
            this.reference = reference;
            this.position = position;
            this.predicted = predicted;
            this.source = source;
            this.candidate = candidate;
            this.prediction = prediction;
            this.interpolation = interpolation;
            motionX = 0;
            motionY = 0;
            Predict(reference, position.X, position.Y, 0, 0, prediction);
            best = Difference(source, prediction, int.MaxValue);
        }

        public static (int X, int Y, bool Exact) Select(H264SourceMacroblock input, H264Picture reference, H264Picture target,
            int x, int y, int range, Span<byte> prediction, CancellationToken token, H264MotionReference? interpolation = null)
        {
            Span<byte> candidate = stackalloc byte[384];
            (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
            H264MotionSearch search = new(reference, (x, y), (predictedX, predictedY), input.Samples, candidate, prediction, interpolation);
            search.Evaluate(Math.Clamp(predictedX / 4, -range, range) * 4, Math.Clamp(predictedY / 4, -range, range) * 4);
            search.Scan(range, token);
            search.Refine(range, token, interpolation);
            return (search.motionX, search.motionY, search.best == 0);
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

        private void Refine(int range, CancellationToken token, H264MotionReference? interpolation)
        {
            if (range == 0 || (best == 0 && motionX == predicted.X && motionY == predicted.Y))
            {
                return;
            }
            int limit = range * 4;
            int originX = motionX;
            int originY = motionY;
            Span<byte> samples = stackalloc byte[H264LumaNeighborhood.SampleBytes];
            Span<byte> filtered = stackalloc byte[H264LumaNeighborhood.FilteredBytes];
            H264LumaNeighborhood neighborhood = interpolation is null
                ? new(reference, (long)position.X + (originX >> 2), (long)position.Y + (originY >> 2), samples, filtered)
                : new(interpolation, position.X + (originX >> 2), position.Y + (originY >> 2));
            ReadOnlySpan<int> offsetsX = [-1, 0, 1, -1, 1, -1, 0, 1];
            ReadOnlySpan<int> offsetsY = [-1, -1, -1, 0, 0, 1, 1, 1];
            for (int step = 2; step >= 1 && range > 0; step /= 2)
            {
                token.ThrowIfCancellationRequested();
                if (best == 0 && motionX == predicted.X && motionY == predicted.Y)
                {
                    break;
                }
                int centerX = motionX;
                int centerY = motionY;
                for (int direction = 0; direction < offsetsX.Length; direction++)
                {
                    int nextX = centerX + offsetsX[direction] * step;
                    int nextY = centerY + offsetsY[direction] * step;
                    if (Math.Abs(nextX) <= limit && Math.Abs(nextY) <= limit && (nextX != motionX || nextY != motionY))
                    {
                        int difference = neighborhood.Evaluate(nextX - originX, nextY - originY, source[..256], candidate[..256], best);
                        Compare(nextX, nextY, difference);
                    }
                }
            }
            Evaluate(Math.Clamp(predicted.X, -limit, limit), Math.Clamp(predicted.Y, -limit, limit));
        }

        private void Evaluate(int nextX, int nextY)
        {
            if (nextX == motionX && nextY == motionY)
            {
                return;
            }
            long x = (long)position.X + (nextX >> 2);
            long y = (long)position.Y + (nextY >> 2);
            if (((nextX | nextY) & 3) == 0 && interpolation is not null)
            {
                int start = checked(((int)y + interpolation.Range + 2) * interpolation.SampleStride + (int)x + interpolation.Range + 2);
                Compare(nextX, nextY, H264SampleDifference.Sum16x16(source[..256], interpolation.Samples[start..], interpolation.SampleStride, best), materializeLuma: true);
                return;
            }
            if (((nextX | nextY) & 3) == 0 && x >= 0 && x <= reference.Width - 16 && y >= 0 && y <= reference.Height - 16)
            {
                ReadOnlySpan<byte> luma = reference.ReadPlane(0).Slice((int)y * reference.Width + (int)x, 15 * reference.Width + 16);
                Compare(nextX, nextY, H264SampleDifference.Sum16x16(source[..256], luma, reference.Width, best), materializeLuma: true);
                return;
            }
            H264PredictionBlock.Read(reference, 0, position.X, position.Y, 16, 16, nextX, nextY, candidate[..256]);
            Compare(nextX, nextY, Difference(source[..256], candidate[..256], best));
        }

        private void Compare(int nextX, int nextY, int difference, bool materializeLuma = false)
        {
            if (difference > best)
            {
                return;
            }
            H264PredictionBlock.Read(reference, 1, position.X, position.Y, 16, 16, nextX, nextY, candidate.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, position.X, position.Y, 16, 16, nextX, nextY, candidate.Slice(320, 64));
            difference += Difference(source[256..], candidate[256..], best - difference);
            if (difference < best || (difference == best &&
                VectorBits(nextX - predicted.X, nextY - predicted.Y) < VectorBits(motionX - predicted.X, motionY - predicted.Y)))
            {
                if (materializeLuma)
                {
                    H264PredictionBlock.Read(reference, 0, position.X, position.Y, 16, 16, nextX, nextY, candidate[..256]);
                }
                best = difference;
                motionX = nextX;
                motionY = nextY;
                candidate.CopyTo(prediction);
            }
        }

        private static int VectorBits(int x, int y) => SignedBits(x) + SignedBits(y);
        private static int SignedBits(int value) => BitOperations.Log2((uint)(Math.Abs(value) * 2 + (value <= 0 ? 1 : 0))) * 2 + 1;

        private static int Difference(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int stop)
        {
            int sum = 0;
            for (int index = 0; index < source.Length; index += Vector128<byte>.Count)
            {
                Vector128<byte> left = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)index);
                Vector128<byte> right = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(prediction), (nuint)index);
                sum += H264SampleDifference.Sum16(left, right);
                if (sum > stop)
                {
                    break;
                }
            }
            return sum;
        }

        private static void Predict(H264Picture reference, int x, int y, int motionX, int motionY, Span<byte> prediction)
        {
            H264PredictionBlock.Read(reference, 0, x, y, 16, 16, motionX, motionY, prediction[..256]);
            H264PredictionBlock.Read(reference, 1, x, y, 16, 16, motionX, motionY, prediction.Slice(256, 64));
            H264PredictionBlock.Read(reference, 2, x, y, 16, 16, motionX, motionY, prediction.Slice(320, 64));
        }
    }
}
