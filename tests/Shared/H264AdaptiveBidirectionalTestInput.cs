// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264AdaptiveBidirectionalTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int depth in new[] { 8, 10 })
            {
                foreach (int chroma in new[] { 1, 2 })
                {
                    VerifyWeights(allocator, depth, chroma);
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Adaptive bidirectional prediction retained picture storage.");
            }
        }

        private static void VerifyWeights(MediaBufferAllocator allocator, int depth, int chroma)
        {
            H264SliceHeader basis = H264BidirectionalPredictionTestInput.Header();
            H264SequenceParameterSet sequence = basis.Sequence with
            {
                WidthInMbs = 1, HeightInMapUnits = 2, Width = 16, Height = 64,
                FrameMbsOnly = false, MbAdaptiveFrameField = true,
                LumaBitDepth = depth, ChromaBitDepth = depth, ChromaFormat = chroma
            };
            H264SliceHeader header = basis with
            {
                Sequence = sequence,
                Weights = new H264WeightTable(0, 0, [new(1, 0, 1, 0, 1, 0), new(2, 3, 2, 3, 2, 3)]),
                WeightsL1 = new H264WeightTable(0, 0, [new(3, 1, 3, 1, 3, 1), new(1, 0, 1, 0, 1, 0)])
            };
            using H264Picture first = new(sequence, allocator, H264SliceKind.P);
            using H264Picture second = new(sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B);
            H264AdaptivePredictionTestInput.Fill(first, 20);
            H264AdaptivePredictionTestInput.Fill(second, 60);
            target.Grid.SetFieldPair(0, false);
            target.Grid.SetFieldPair(2, true);
            H264ReferencePicture[] frames = [new(first, 0, new(0, 2)), new(second, 1, new(8, 10))];
            H264AdaptiveReferences references = new(new(frames, frames));
            byte[] prediction = new byte[target.MacroblockBytes];
            for (int address = 0; address < 4; address++)
            {
                bool field = address >= 2;
                (int x, int y) = target.Grid.Position(address);
                H264MacroblockPredictionContext context = H264MacroblockPredictionContext.Create(target, x, y);
                for (int opposite = 0; opposite < (field ? 2 : 1); opposite++)
                {
                    for (int weighting = 0; weighting < 3; weighting++)
                    {
                        for (int selection = 0; selection < 3; selection++)
                        {
                            H264PartitionMotion l0 = selection == 1 ? H264PartitionMotion.None : new(field ? 2 + opposite : 1, 0, 0);
                            H264PartitionMotion l1 = selection == 0 ? H264PartitionMotion.None : new(field ? opposite : 0, 0, 0);
                            H264BidirectionalPrediction.PredictPartition(target,
                                header with { Picture = header.Picture with { WeightedBiPrediction = weighting } },
                                references.Get(context.Structure), new(3, 6), x, y, 16, 16, l0, l1, prediction);
                            VerifySamples(target, prediction, address, opposite, weighting, selection);
                        }
                    }
                }
            }
        }

        private static void VerifySamples(H264Picture target, ReadOnlySpan<byte> prediction, int address,
            int opposite, int weighting, int selection)
        {
            bool field = address >= 2;
            int scale = 1 << (target.Sequence.LumaBitDepth - 8);
            int position = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int columns = plane == 0 ? 16 : 8;
                for (int row = 0; row < 16 / target.VerticalScale(plane); row++)
                {
                    int parity = field ? (address + opposite) & 1 : row & 1;
                    int first = (60 + parity * 20) * scale;
                    int second = (20 + parity * 20) * scale;
                    int weight1 = (field, address & 1, opposite) switch
                    {
                        (false, _, _) or (true, 0, 0) => 40,
                        (true, 1, 0) => 32,
                        (true, 0, 1) => 56,
                        (true, 1, 1) => 16,
                        _ => throw new InvalidOperationException("Invalid implicit weight case.")
                    };
                    int expected = (weighting, selection) switch
                    {
                        (0 or 2, 0) => first,
                        (0 or 2, 1) => second,
                        (0, 2) => (first + second + 1) / 2,
                        (1, 0) => first * 2 + 3 * scale,
                        (1, 1) => second * 3 + scale,
                        (1, 2) => (first * 2 + second * 3 + 1) / 2 + 2 * scale,
                        (2, 2) => (first * (64 - weight1) + second * weight1 + 32) / 64,
                        _ => throw new InvalidOperationException("Invalid bidirectional weight case.")
                    };
                    ReadOnlySpan<byte> line = prediction.Slice(position, columns * target.SampleBytes);
                    bool matches = target.SampleBytes == 1
                        ? line.IndexOfAnyExcept((byte)expected) < 0
                        : MemoryMarshal.Cast<byte, ushort>(line).IndexOfAnyExcept((ushort)expected) < 0;
                    if (!matches)
                    {
                        throw new InvalidDataException($"Adaptive B weights differ: address={address}, parity={opposite}, weighting={weighting}, selection={selection}.");
                    }
                    position += line.Length;
                }
            }
        }
    }
}
