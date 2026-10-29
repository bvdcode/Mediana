// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalPredictionTests
    {
        [Fact]
        public void BothSingleListsAndAllBWeightModesPreservePartitionPlacementAndFractionalMotion() =>
            H264BidirectionalPredictionTestInput.Verify();

        [Theory]
        [InlineData(0, 0, 16, 16)]
        [InlineData(0, 8, 16, 8)]
        [InlineData(8, 0, 8, 16)]
        [InlineData(8, 8, 8, 8)]
        [InlineData(8, 12, 8, 4)]
        [InlineData(12, 8, 4, 8)]
        [InlineData(12, 12, 4, 4)]
        public void EveryBPartitionShapeWritesOnlyItsLumaAndChromaRectangle(int x, int y, int width, int height)
        {
            H264SliceHeader header = H264BidirectionalPredictionTestInput.Header();
            MediaBufferAllocator allocator = new(2048);
            using H264Picture reference = new(header.Sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(header.Sequence, allocator, H264SliceKind.B);
            for (int plane = 0; plane < 3; plane++)
            {
                reference.Plane(plane).Fill(120);
            }
            H264BidirectionalReferences references = new([new(reference, 0, new(0, 0))], [new(reference, 0, new(0, 0))]);
            byte[] prediction = Enumerable.Repeat((byte)204, 384).ToArray();
            H264BidirectionalPrediction.PredictPartition(target, header, references, new(2, 2), x, y, width, height,
                new(0, 0, 0), new(0, 0, 0), prediction);
            int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int scale = plane == 0 ? 1 : 2;
                int size = 16 / scale;
                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column++)
                    {
                        int expected = column >= x / scale && column < (x + width) / scale
                            && row >= y / scale && row < (y + height) / scale ? 120 : 204;
                        Assert.Equal(expected, prediction[offset + row * size + column]);
                    }
                }
                offset += size * size;
            }
        }

        [Theory]
        [InlineData(-1, -1)]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        public void MissingReferencesAreRejectedBeforeMotionAndPredictionWrites(int index0, int index1)
        {
            H264SliceHeader header = H264BidirectionalPredictionTestInput.Header();
            MediaBufferAllocator allocator = new(2048);
            using H264Picture reference = new(header.Sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(header.Sequence, allocator, H264SliceKind.B);
            H264BidirectionalReferences references = new([new(reference, 0, new(0, 0))], [new(reference, 0, new(0, 0))]);
            byte[] prediction = Enumerable.Repeat((byte)204, 384).ToArray();
            Assert.Throws<InvalidDataException>(() => H264BidirectionalPrediction.PredictPartition(target, header, references, new(2, 2),
                0, 0, 16, 16, new(index0, 0, 0), new(index1, 0, 0), prediction));
            Assert.All(prediction, value => Assert.Equal(204, value));
            Assert.False(target.MotionAt(H264ReferenceList.L0, 0, 0).Available);
        }

        [Fact]
        public void ExplicitPredictionCannotSubstituteForMissingListWeights()
        {
            H264SliceHeader header = H264BidirectionalPredictionTestInput.Header();
            header = header with { Picture = header.Picture with { WeightedBiPrediction = 1 }, WeightsL1 = null };
            MediaBufferAllocator allocator = new(2048);
            using H264Picture reference = new(header.Sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(header.Sequence, allocator, H264SliceKind.B);
            H264BidirectionalReferences references = new([new(reference, 0, new(0, 0))], [new(reference, 0, new(0, 0))]);
            Assert.Throws<InvalidDataException>(() => H264BidirectionalPrediction.PredictPartition(target, header, references, new(2, 2),
                0, 0, 16, 16, new(0, 0, 0), new(0, 0, 0), new byte[384]));
        }
    }
}
