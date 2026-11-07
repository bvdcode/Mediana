// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Hevc
{
    /// <summary>Combines positive fourteen-bit interpolation samples according to H.265 8.5.3.3.4.</summary>
    internal static class HevcWeightedPrediction
    {
        public static void Unidirectional(ReadOnlySpan<int> prediction, Span<int> output, int bitDepth,
            int denominator = 0, int weight = 1, int offset = 0)
        {
            Validate(bitDepth, denominator, weight, offset);
            if (output.Length < prediction.Length) { throw new ArgumentException("Prediction output is too short.", nameof(output)); }
            int shift = denominator + 14 - bitDepth, rounding = 1 << (shift - 1);
            int scaledOffset = offset << (bitDepth - 8), maximum = (1 << bitDepth) - 1;
            int i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<int> weights = new(weight), round = new(rounding), offsets = new(scaledOffset), limit = new(maximum);
                for (; i <= prediction.Length - Vector<int>.Count; i += Vector<int>.Count)
                {
                    Vector<int> samples = ((new Vector<int>(prediction.Slice(i)) * weights + round) >> shift) + offsets;
                    Vector.Min(limit, Vector.Max(Vector<int>.Zero, samples)).CopyTo(output.Slice(i));
                }
            }
            for (; i < prediction.Length; i++)
            {
                output[i] = Math.Clamp(((prediction[i] * weight + rounding) >> shift) + scaledOffset, 0, maximum);
            }
        }

        public static void Bidirectional(ReadOnlySpan<int> first, ReadOnlySpan<int> second, Span<int> output,
            int bitDepth, int denominator = 0, int firstWeight = 1, int secondWeight = 1,
            int firstOffset = 0, int secondOffset = 0)
        {
            Validate(bitDepth, denominator, firstWeight, firstOffset);
            Validate(bitDepth, denominator, secondWeight, secondOffset);
            if (second.Length != first.Length || output.Length < first.Length)
            { throw new ArgumentException("Bidirectional prediction buffers have inconsistent lengths."); }
            int log2Weight = denominator + 14 - bitDepth, shift = log2Weight + 1;
            int bias = (((firstOffset + secondOffset) << (bitDepth - 8)) + 1) << log2Weight;
            int maximum = (1 << bitDepth) - 1, i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<int> weight0 = new(firstWeight), weight1 = new(secondWeight), round = new(bias), limit = new(maximum);
                for (; i <= first.Length - Vector<int>.Count; i += Vector<int>.Count)
                {
                    Vector<int> samples = (new Vector<int>(first.Slice(i)) * weight0 +
                        new Vector<int>(second.Slice(i)) * weight1 + round) >> shift;
                    Vector.Min(limit, Vector.Max(Vector<int>.Zero, samples)).CopyTo(output.Slice(i));
                }
            }
            for (; i < first.Length; i++)
            {
                output[i] = Math.Clamp((first[i] * firstWeight + second[i] * secondWeight + bias) >> shift, 0, maximum);
            }
        }

        private static void Validate(int bitDepth, int denominator, int weight, int offset)
        {
            if (bitDepth is not (8 or 10)) { throw new ArgumentOutOfRangeException(nameof(bitDepth)); }
            ArgumentOutOfRangeException.ThrowIfNegative(denominator);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(denominator, 7);
            if (weight < (1 << denominator) - 128 || weight > (1 << denominator) + 127)
            { throw new ArgumentOutOfRangeException(nameof(weight)); }
            if (offset is < -128 or > 127) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        }
    }
}
