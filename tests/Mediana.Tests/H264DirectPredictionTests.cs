// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264DirectPredictionTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void TemporalDirectMapsColocatedL0OrL1ToTheFirstCurrentReference(bool bidirectional, bool inference)
        {
            H264SequenceParameterSet sequence = Sequence(inference);
            MediaBufferAllocator allocator = new(32768);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B);
            using H264Picture past = new(sequence, allocator, H264SliceKind.I);
            using H264Picture other = new(sequence, allocator, H264SliceKind.I);
            using H264Picture future = new(sequence, allocator, bidirectional ? H264SliceKind.B : H264SliceKind.P);
            H264ReferencePicture pastReference = new(past, 0, new H264PictureOrder(0, 0));
            H264ReferencePicture otherReference = new(other, 1, new H264PictureOrder(-2, -2));
            H264ReferencePicture futureReference = new(future, 2, new H264PictureOrder(8, 8));
            H264ReferenceList list = bidirectional ? H264ReferenceList.L1 : H264ReferenceList.L0;
            future.SetMotion(list, 0, 0, 48, 48, 1, 12, -8);
            future.CaptureReferences(bidirectional ? [] : [otherReference, pastReference], bidirectional ? [otherReference, pastReference] : []);
            H264SliceHeader header = Header(sequence);
            H264BidirectionalReferences references = new([otherReference, pastReference, pastReference], [futureReference]);
            Span<byte> prediction = stackalloc byte[384];
            H264DirectPrediction.Predict(target, header, references, new H264PictureOrder(2, 2), 16, 16, prediction);
            Assert.Equal(new H264MotionSample(true, 1, 3, -2), target.MotionAt(H264ReferenceList.L0, 16, 16));
            Assert.Equal(new H264MotionSample(true, 0, -9, 6), target.MotionAt(H264ReferenceList.L1, 20, 20));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SpatialDirectUsesNeighbourMinimumAndColocatedZeroForReferenceZero(bool longTerm)
        {
            H264SequenceParameterSet sequence = Sequence(true);
            MediaBufferAllocator allocator = new(32768);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B);
            using H264Picture past = new(sequence, allocator, H264SliceKind.I);
            using H264Picture future = new(sequence, allocator, H264SliceKind.P);
            future.SetMotion(H264ReferenceList.L0, 0, 0, 48, 48, 0, 1, -1);
            foreach ((int x, int y) in new[] { (12, 16), (16, 12), (32, 12) })
            {
                target.SetMotion(H264ReferenceList.L0, x, y, 4, 4, 1, 12, 8);
                target.SetMotion(H264ReferenceList.L1, x, y, 4, 4, 0, -8, 4);
            }
            H264SliceHeader header = Header(sequence) with { DirectSpatialMotionPrediction = true };
            H264ReferencePicture pastReference = new(past, 0, new H264PictureOrder(0, 0));
            H264ReferencePicture futureReference = new(future, 1, new H264PictureOrder(8, 8), longTerm ? 0 : null);
            Span<byte> prediction = stackalloc byte[384];
            H264DirectPrediction.Predict(target, header, new([pastReference, pastReference], [futureReference]), new(2, 2), 16, 16, prediction);
            Assert.Equal(new H264MotionSample(true, 1, 12, 8), target.MotionAt(H264ReferenceList.L0, 16, 16));
            Assert.Equal(new H264MotionSample(true, 0, longTerm ? -8 : 0, longTerm ? 4 : 0), target.MotionAt(H264ReferenceList.L1, 16, 16));
        }

        [Fact]
        public void TemporalDirectRejectsMissingReferenceIdentity()
        {
            H264SequenceParameterSet sequence = Sequence(true);
            MediaBufferAllocator allocator = new(32768);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B);
            using H264Picture future = new(sequence, allocator, H264SliceKind.P);
            future.SetMotion(H264ReferenceList.L0, 0, 0, 48, 48, 0, 12, -8);
            H264ReferencePicture reference = new(future, 0, new H264PictureOrder(8, 8));
            Assert.Throws<InvalidDataException>(() => H264DirectPrediction.Predict(target, Header(sequence), new([reference], [reference]), new(2, 2), 16, 16, new byte[384]));
        }

        private static H264SequenceParameterSet Sequence(bool inference) => new()
        {
            WidthInMbs = 3,
            HeightInMapUnits = 3,
            FrameMbsOnly = true,
            Direct8x8Inference = inference
        };

        private static H264SliceHeader Header(H264SequenceParameterSet sequence) => new(sequence,
            new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 2, 1, false, 0))
        { Kind = H264SliceKind.B, ActiveReferencesL0 = 2, ActiveReferencesL1 = 1 };
    }
}
