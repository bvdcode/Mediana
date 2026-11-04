// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264BiPredictionVectorTestInput
    {
        public static void Verify()
        {
            (int First, int Second)[] pairs = [(-128, 0), (-64, 128), (0, 0), (1, 1), (128, 0),
                (0, 128), (63, 64), (127, -128), (-1, -127), (32, 32), (43, 21), (64, 64),
                (127, 1), (-128, 127), (127, 0)];
            int[] offsets = [-128, -1, 0, 1, 127];
            int[] lengths = [0, 1, 2, 3, 4, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257];
            int cases = 0;
            for (int denominator = 0; denominator <= 7; denominator++)
            {
                foreach ((int first, int second) in pairs)
                {
                    if (first + second > (denominator == 7 ? 127 : 128))
                    {
                        continue;
                    }
                    foreach (int offset in offsets)
                    {
                        H264BiPredictionWeights weights = Create(denominator, first, second, offset, offset);
                        VerifyBlock(weights, 65536, cases % 31 + 1, 0);
                        foreach (int length in lengths)
                        {
                            for (int alias = 0; alias < 3; alias++)
                            {
                                VerifyBlock(weights, length, cases % 31 + 1, alias);
                            }
                        }
                        cases++;
                    }
                }
            }
            foreach (int offset in offsets)
            {
                H264BiPredictionWeights weights = Create(0, 1, 1, offset, offset);
                VerifyBlock(weights, 65536, 3, 0);
                cases++;
            }
            VerifyBlock(H264BiPredictionWeights.Average, 65536, 5, 0);
            VerifyOverlap();
            VerifyAllocation();
            VerifySharedInput();
        }

        private static H264BiPredictionWeights Create(int denominator, int first, int second, int offset0, int offset1)
        {
            H264WeightTable l0 = new(denominator, denominator, [new(first, offset0, first, offset0, first, offset0)]);
            H264WeightTable l1 = new(denominator, denominator, [new(second, offset1, second, offset1, second, offset1)]);
            return H264BiPredictionWeights.Explicit(l0, 0, l1, 0, 0);
        }

        private static void VerifyBlock(H264BiPredictionWeights weights, int length, int prefix, int alias)
        {
            byte[] first = new byte[length + prefix + 33];
            byte[] second = new byte[first.Length];
            byte[] output = new byte[first.Length];
            first.AsSpan().Fill(0xA3);
            second.AsSpan().Fill(0xC7);
            output.AsSpan().Fill(0xE9);
            byte[] expected = new byte[length];
            for (int index = 0; index < length; index++)
            {
                byte x = (byte)(index >> 8);
                byte y = (byte)index;
                if (length < 65536)
                {
                    x = (byte)(index * 73 + prefix * 29);
                    y = (byte)(index * 131 + prefix * 47);
                }
                first[prefix + index] = x;
                second[prefix + index] = y;
                long numerator = (long)x * weights.Weight0 + (long)y * weights.Weight1 + (1L << weights.Denominator);
                long value = (long)Math.Floor((double)numerator / (1L << (weights.Denominator + 1))) + weights.Offset;
                expected[index] = (byte)Math.Clamp(value, 0, 255);
            }
            byte[] firstCopy = (byte[])first.Clone();
            byte[] secondCopy = (byte[])second.Clone();
            byte[] target = alias switch { 0 => output, 1 => first, 2 => second, _ => throw new ArgumentOutOfRangeException(nameof(alias)) };
            byte guard = alias switch { 0 => (byte)0xE9, 1 => (byte)0xA3, 2 => (byte)0xC7, _ => throw new ArgumentOutOfRangeException(nameof(alias)) };
            weights.Apply(first.AsSpan(prefix, length), second.AsSpan(prefix, length), target.AsSpan(prefix, length));
            if (!target.AsSpan(prefix, length).SequenceEqual(expected))
            {
                throw new InvalidDataException($"Weighted prediction differs: denominator={weights.Denominator}; w0={weights.Weight0}; w1={weights.Weight1}; length={length}; alias={alias}.");
            }
            if (target.AsSpan(0, prefix).ContainsAnyExcept(guard) || target.AsSpan(prefix + length).ContainsAnyExcept(guard))
            {
                throw new InvalidDataException("Prediction writes outside the output extent.");
            }
            if ((alias != 1 && !first.AsSpan().SequenceEqual(firstCopy)) || (alias != 2 && !second.AsSpan().SequenceEqual(secondCopy)))
            {
                throw new InvalidDataException("Prediction changes a borrowed input.");
            }
        }

        private static void VerifyAllocation()
        {
            byte[] first = new byte[64];
            byte[] second = new byte[64];
            byte[] output = new byte[64];
            H264BiPredictionWeights weights = Create(5, 43, 21, -1, -1);
            for (int index = 0; index < 64; index++)
            {
                weights.Apply(first, second, output);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 256; index++)
            {
                weights.Apply(first, second, output);
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before)
            {
                throw new InvalidDataException("Bidirectional prediction allocates per block.");
            }
        }

        private static void VerifySharedInput()
        {
            H264BiPredictionWeights weights = Create(2, -64, 128, -3, -3);
            byte[] samples = new byte[65];
            byte[] expected = new byte[samples.Length];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (byte)(index * 73);
                long numerator = (long)samples[index] * (weights.Weight0 + weights.Weight1) + (1L << weights.Denominator);
                expected[index] = (byte)Math.Clamp((long)Math.Floor((double)numerator / (1L << (weights.Denominator + 1))) + weights.Offset, 0, 255);
            }
            weights.Apply(samples, samples, samples);
            if (!samples.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException("Prediction fails when both inputs and output share storage.");
            }
        }

        private static void VerifyOverlap()
        {
            byte[] shared = new byte[65];
            byte[] other = new byte[64];
            bool rejected = false;
            try
            {
                H264BiPredictionWeights.Average.Apply(shared.AsSpan(0, 64), other, shared.AsSpan(1, 64));
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidDataException("A partial input/output overlap was accepted.");
            }
        }
    }
}
