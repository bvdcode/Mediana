// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264BiPredictionWeights
    {
        private H264BiPredictionWeights(int weight0, int weight1, int offset, int denominator)
        {
            Weight0 = weight0;
            Weight1 = weight1;
            Offset = offset;
            Denominator = denominator;
        }

        public int Weight0 { get; }
        public int Weight1 { get; }
        public int Offset { get; }
        public int Denominator { get; }
        public static H264BiPredictionWeights Average => new(1, 1, 0, 0);

        public static H264BiPredictionWeights Explicit(H264WeightTable l0, int reference0, H264WeightTable l1, int reference1, int plane)
        {
            (int weight0, int offset0, int denominator) = l0.Get(reference0, plane);
            (int weight1, int offset1, int denominator1) = l1.Get(reference1, plane);
            int maximumSum = denominator == 7 ? 127 : 128;
            if (denominator is < 0 or > 7 || denominator != denominator1 || weight0 is < -128 or > 128
                || weight1 is < -128 or > 128 || weight0 + weight1 < -128 || weight0 + weight1 > maximumSum)
            {
                throw new InvalidDataException("Invalid H.264 explicit bidirectional prediction weights.");
            }
            return new H264BiPredictionWeights(weight0, weight1, (offset0 + offset1 + 1) >> 1, denominator);
        }

        public static H264BiPredictionWeights Implicit(H264PictureOrder current, H264ReferencePicture l0, H264ReferencePicture l1)
        {
            H264BiPredictionWeights equal = new(32, 32, 0, 5);
            long difference = (long)l1.Order.Value - l0.Order.Value;
            if (difference == 0 || l0.LongTermIndex is not null || l1.LongTermIndex is not null)
            {
                return equal;
            }
            int td = (int)Math.Clamp(difference, -128, 127);
            int tb = (int)Math.Clamp((long)current.Value - l0.Order.Value, -128, 127);
            int reciprocal = (16384 + Math.Abs(td / 2)) / td;
            int scale = Math.Clamp((tb * reciprocal + 32) >> 6, -1024, 1023);
            int weight1 = scale >> 2;
            return weight1 is < -64 or > 128 ? equal : new H264BiPredictionWeights(64 - weight1, weight1, 0, 5);
        }

        public void Apply(ReadOnlySpan<byte> l0, ReadOnlySpan<byte> l1, Span<byte> output)
        {
            if (l0.Length != l1.Length || l0.Length != output.Length)
            {
                throw new ArgumentException("H.264 bidirectional prediction blocks must have equal lengths.");
            }
            if ((l0.Overlaps(output, out int offset0) && offset0 != 0) || (l1.Overlaps(output, out int offset1) && offset1 != 0))
            {
                throw new ArgumentException("H.264 bidirectional prediction blocks cannot partially overlap their output.");
            }
            int rounding = 1 << Denominator;
            for (int index = 0; index < output.Length; index++)
            {
                output[index] = (byte)Math.Clamp(((l0[index] * Weight0 + l1[index] * Weight1 + rounding) >> (Denominator + 1)) + Offset, 0, 255);
            }
        }
    }
}
