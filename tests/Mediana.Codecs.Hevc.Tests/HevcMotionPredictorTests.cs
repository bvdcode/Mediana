// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcMotionPredictorTests
    {
        [Fact]
        public async Task SpatialPredictorsPreferMatchingReferenceAndKeepDistinctLeftAndTopVectors()
        {
            HevcSliceHeader slice = (await HeadersAsync()).First(header => header.Type == HevcSliceType.B);
            SequenceParameters sequence = slice.Picture.Sequence;
            MediaBufferAllocator allocator = new(1024 * 1024);
            using HevcReferencePicture previous = HevcReferenceBufferTests.Picture(sequence, 2, allocator);
            using HevcReferencePicture older = HevcReferenceBufferTests.Picture(sequence, 0, allocator);
            using HevcReferencePicture future = HevcReferenceBufferTests.Picture(sequence, 6, allocator);
            HevcReferenceLists references = new([new(previous, false), new(older, false)], [new(future, false), new(previous, false)]);
            using HevcMotionField motion = new(sequence.Width, sequence.Height, allocator);
            HevcMotionNeighbours neighbours = new(slice, motion, null);
            HevcMotionPredictor predictor = new(slice, 4, references, neighbours);
            HevcPredictionBlock block = new(16, 16, 16, 16, 16, 16, 16, 0, HevcPartitionMode.Whole);
            motion.Set(12, 28, 4, 4, references.Create(0, new(12, -4), -1, default));
            motion.Set(28, 12, 4, 4, references.Create(-1, default, 1, new(3, 7)));
            Assert.Equal(new HevcMotionVector(12, -4), predictor.Predict(block, 0, 0, false));
            Assert.Equal(new HevcMotionVector(3, 7), predictor.Predict(block, 0, 0, true));
            motion.Set(28, 12, 4, 4, references.Create(-1, default, 1, new(12, -4)));
            Assert.Equal(default, predictor.Predict(block, 0, 0, true));
        }

        [Fact]
        public async Task SpatialScalingUsesPictureDistancesAndRejectsIncompatibleLongTermStatus()
        {
            HevcSliceHeader slice = (await HeadersAsync()).First(header => header.Type == HevcSliceType.P);
            SequenceParameters sequence = slice.Picture.Sequence;
            MediaBufferAllocator allocator = new(1024 * 1024);
            using HevcReferencePicture previous = HevcReferenceBufferTests.Picture(sequence, 2, allocator);
            using HevcReferencePicture older = HevcReferenceBufferTests.Picture(sequence, 0, allocator);
            HevcReferenceLists references = new([new(previous, false), new(older, false)], []);
            using HevcMotionField motion = new(sequence.Width, sequence.Height, allocator);
            HevcMotionNeighbours neighbours = new(slice, motion, null);
            HevcMotionPredictor predictor = new(slice, 4, references, neighbours);
            HevcPredictionBlock block = new(16, 16, 16, 16, 16, 16, 16, 0, HevcPartitionMode.Whole);
            motion.Set(12, 28, 4, 4, references.Create(1, new(24, -8), -1, default));
            Assert.Equal(new HevcMotionVector(12, -4), predictor.Predict(block, 0, 0, false));
            HevcMotionSample incompatible = references.Create(1, new(24, -8), -1, default);
            motion.Set(12, 28, 4, 4, incompatible with { Flags = (byte)(incompatible.Flags | HevcMotionSample.LongTerm0) });
            Assert.Equal(default, predictor.Predict(block, 0, 0, false));
        }

        [Fact]
        public async Task MergeUsesSpatialOrderRemovesDuplicatesAndFillsWithZeroMotion()
        {
            HevcSliceHeader slice = (await HeadersAsync()).First(header => header.Type == HevcSliceType.P);
            SequenceParameters sequence = slice.Picture.Sequence;
            MediaBufferAllocator allocator = new(1024 * 1024);
            using HevcReferencePicture previous = HevcReferenceBufferTests.Picture(sequence, 2, allocator);
            using HevcReferencePicture older = HevcReferenceBufferTests.Picture(sequence, 0, allocator);
            HevcReferenceLists references = new([new(previous, false), new(older, false)], []);
            using HevcMotionField motion = new(sequence.Width, sequence.Height, allocator);
            HevcMotionNeighbours neighbours = new(slice, motion, null);
            HevcMotionPredictor predictor = new(slice, 4, references, neighbours);
            HevcMergeCandidates merge = new(slice, references, neighbours, predictor);
            HevcPredictionBlock block = new(16, 16, 16, 16, 16, 16, 16, 0, HevcPartitionMode.Whole);
            HevcMotionSample left = references.Create(0, new(5, -3), -1, default);
            motion.Set(12, 28, 4, 4, left); motion.Set(28, 12, 4, 4, left);
            motion.Set(12, 12, 4, 4, references.Create(1, new(-2, 7), -1, default));
            Assert.Equal(left, merge.Get(block, 0));
            Assert.Equal(new HevcMotionVector(-2, 7), merge.Get(block, 1).Vector0);
            Assert.Equal(references.Create(0, default, -1, default), merge.Get(block, 2));
            Assert.Throws<InvalidDataException>(() => merge.Get(block, slice.MaximumMergeCandidates));
        }

        [Fact]
        public async Task ZScanAvailabilityDoesNotExposeFutureCodingBlocks()
        {
            HevcSliceHeader slice = (await HeadersAsync())[0];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using HevcMotionField motion = new(64, 48, allocator);
            HevcMotionSample value = new(0, 0, new(1, 2), default, 0, -1, HevcMotionSample.List0);
            motion.Set(0, 0, 64, 48, value);
            HevcMotionNeighbours neighbours = new(slice, motion, null);
            HevcPredictionBlock block = new(16, 16, 16, 16, 16, 16, 16, 0, HevcPartitionMode.Whole);
            Assert.True(neighbours.Get(block, 15, 31).IsInter);
            Assert.True(neighbours.Get(block, 31, 15).IsInter);
            Assert.False(neighbours.Get(block, 32, 15).IsInter);
            Assert.False(neighbours.Get(block, 15, 32).IsInter);
            HevcPredictionBlock quarter = new(16, 16, 16, 24, 16, 8, 8, 1, HevcPartitionMode.Quarters);
            Assert.False(neighbours.Get(quarter, 23, 24).IsInter);
        }

        private static Task<HevcSliceHeader[]> HeadersAsync() =>
            HevcSliceHeaderTestInput.ReadHeadersAsync(Path.Combine(AppContext.BaseDirectory, "Data"), "main8");
    }
}
