// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Video;

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
        private int best;
        private int motionX;
        private int motionY;

        private H264MotionSearch(H264Picture reference, (int X, int Y) position, (int X, int Y) predicted,
            ReadOnlySpan<byte> source, Span<byte> candidate, Span<byte> prediction)
        {
            this.reference = reference;
            this.position = position;
            this.predicted = predicted;
            this.source = source;
            this.candidate = candidate;
            this.prediction = prediction;
            motionX = 0;
            motionY = 0;
            Predict(reference, position.X, position.Y, 0, 0, prediction);
            best = Difference(source, prediction, int.MaxValue);
        }

        public static (int X, int Y, bool Exact) Select(VideoFrame input, H264Picture reference, H264Picture target,
            int x, int y, int range, Span<byte> prediction, CancellationToken token)
        {
            Span<byte> source = stackalloc byte[384];
            ReadSource(input, x, y, source);
            Span<byte> candidate = stackalloc byte[384];
            (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
            H264MotionSearch search = new(reference, (x, y), (predictedX, predictedY), source, candidate, prediction);
            search.Evaluate(Math.Clamp(predictedX / 4, -range, range) * 4, Math.Clamp(predictedY / 4, -range, range) * 4);
            search.Scan(range, token);
            search.Refine(range, token);
            return (search.motionX, search.motionY, search.best == 0);
        }

        private void Scan(int range, CancellationToken token)
        {
            int limit = range * 4;
            ReadOnlySpan<int> stepsX = [-4, 4, 0, 0];
            ReadOnlySpan<int> stepsY = [0, 0, -4, 4];
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
                    if (Math.Abs(nextX) <= limit && Math.Abs(nextY) <= limit)
                    {
                        Evaluate(nextX, nextY);
                    }
                }
                if (centerX == motionX && centerY == motionY)
                {
                    break;
                }
            }
        }

        private void Refine(int range, CancellationToken token)
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
            H264LumaNeighborhood neighborhood = new(reference, (long)position.X + (originX >> 2),
                (long)position.Y + (originY >> 2), samples, filtered);
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
                        neighborhood.Read(nextX - originX, nextY - originY, candidate[..256]);
                        Compare(nextX, nextY);
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
            H264PredictionBlock.Read(reference, 0, position.X, position.Y, 16, 16, nextX, nextY, candidate[..256]);
            Compare(nextX, nextY);
        }

        private void Compare(int nextX, int nextY)
        {
            int difference = Difference(source[..256], candidate[..256], best);
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
                Vector128<byte> difference = Vector128.Subtract(Vector128.Max(left, right), Vector128.Min(left, right));
                sum += Vector128.Sum(Vector128.WidenLower(difference)) + Vector128.Sum(Vector128.WidenUpper(difference));
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

        private static void ReadSource(VideoFrame input, int x, int y, Span<byte> source)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int width = 16 / scale;
                int start = plane switch { 0 => 0, 1 => 256, 2 => 320, _ => throw new ArgumentOutOfRangeException(nameof(plane)) };
                H264BorderExtension.Copy(input, plane, x / scale, y / scale, width, width, source.Slice(start, width * width));
            }
        }
    }
}
