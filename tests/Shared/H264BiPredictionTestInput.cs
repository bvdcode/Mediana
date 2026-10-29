// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264BiPredictionTestInput
    {
        public static void Verify()
        {
            byte[] l0 = [0, 1, 2, 127, 128, 254, 255];
            byte[] l1 = [255, 254, 128, 127, 2, 1, 0];
            byte[] output = new byte[l0.Length];
            VerifyBlock(H264BiPredictionWeights.Average, l0, l1, output, [128, 128, 65, 127, 65, 128, 128]);
            H264WeightTable weights0 = new(1, 2, [new(-1, -3, 2, 1, 0, 1)]);
            H264WeightTable weights1 = new(1, 2, [new(3, 2, 5, -4, 2, -2)]);
            VerifyBlock(H264BiPredictionWeights.Explicit(weights0, 0, weights1, 0, 0), l0, l1, output, [191, 190, 96, 64, 0, 0, 0]);
            VerifyBlock(H264BiPredictionWeights.Explicit(weights0, 0, weights1, 0, 1), l0, l1, output, [158, 158, 80, 110, 32, 63, 63]);
            VerifyBlock(H264BiPredictionWeights.Explicit(weights0, 0, weights1, 0, 2), l0, l1, output, [64, 64, 32, 32, 1, 0, 0]);
            MediaBufferAllocator allocator = new(2048);
            H264SequenceParameterSet sequence = new() { WidthInMbs = 1, HeightInMapUnits = 1, FrameMbsOnly = true };
            using (H264Picture first = new(sequence, allocator, H264SliceKind.P))
            using (H264Picture second = new(sequence, allocator, H264SliceKind.P))
            {
                H264ReferencePicture before = new(first, 0, new(0, 0));
                H264ReferencePicture after = new(second, 1, new(6, 6));
                H264BiPredictionWeights weights = H264BiPredictionWeights.Implicit(new(2, 2), before, after);
                if (weights.Weight0 != 43 || weights.Weight1 != 21 || weights.Denominator != 5 || weights.Offset != 0)
                {
                    throw new InvalidDataException("H.264 implicit weights differ from the normative POC scaling vector.");
                }
                VerifyBlock(weights, l0, l1, output, [84, 84, 43, 127, 87, 171, 171]);
                after = after with { Order = new H264PictureOrder(1, 1) };
                VerifyBlock(H264BiPredictionWeights.Implicit(new(-1, -1), before, after), l0, l1, output, [0, 0, 0, 127, 254, 255, 255]);
                VerifyBlock(H264BiPredictionWeights.Implicit(new(2, 2), before, after), l0, l1, output, [255, 255, 254, 127, 0, 0, 0]);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Bidirectional sample prediction verification retained picture storage.");
            }
        }

        private static void VerifyBlock(H264BiPredictionWeights weights, ReadOnlySpan<byte> l0, ReadOnlySpan<byte> l1,
            Span<byte> output, ReadOnlySpan<byte> expected)
        {
            weights.Apply(l0, l1, output);
            if (!output.SequenceEqual(expected))
            {
                throw new InvalidDataException("H.264 bidirectional prediction differs from the expected 8-bit sample vector.");
            }
        }
    }
}
