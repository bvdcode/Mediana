// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264MixedDirectPredictionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FieldDirectUsesMatchingParityAndTruncatesNegativeOddVerticalMotion(bool bottom)
        {
            H264PictureStructure structure = bottom ? H264PictureStructure.BottomField : H264PictureStructure.TopField;
            H264SequenceParameterSet sequence = Sequence();
            MediaBufferAllocator allocator = new(65536);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B, structure);
            using H264Picture past = new(sequence, allocator, H264SliceKind.I);
            using H264Picture future = new(sequence, allocator, H264SliceKind.P);
            H264ReferencePicture pastFrame = new(past, 0, new(0, 1), 0);
            H264ReferencePicture futureFrame = new(future, 1, new(4, 5));
            H264PictureStructure opposite = structure switch
            {
                H264PictureStructure.TopField => H264PictureStructure.BottomField,
                H264PictureStructure.BottomField => H264PictureStructure.TopField,
                _ => throw new ArgumentOutOfRangeException(nameof(structure))
            };
            future.SetMotion(H264ReferenceList.L0, 0, 0, 32, 64, 0, 4, 8);
            future.SetMotion(H264ReferenceList.L0, 16, 32, 16, 16, 0, 12, -3);
            future.CaptureReferences(0, [pastFrame], []);
            H264BidirectionalReferences references = new([pastFrame.GetField(opposite), pastFrame.GetField(structure)], [futureFrame.GetField(structure)]);
            Span<byte> prediction = stackalloc byte[384];
            H264DirectPrediction.Predict(target, Header(sequence, structure), references, new(2, 3, structure), 16, 16, prediction);
            Assert.Equal(new H264MotionSample(true, 1, 12, -1), target.MotionAt(H264ReferenceList.L0, 16, 16));
            Assert.Equal(new H264MotionSample(true, 0, 0, 0), target.MotionAt(H264ReferenceList.L1, 16, 16));
        }

        [Theory]
        [InlineData(4, 4, 2)]
        [InlineData(5, 12, -6)]
        [InlineData(7, 12, -6)]
        public void FrameDirectSelectsNearestFieldWithBottomParityOnTies(int currentOrder, int motionX, int motionY)
        {
            H264SequenceParameterSet sequence = Sequence();
            MediaBufferAllocator allocator = new(65536);
            using H264Picture target = new(sequence, allocator, H264SliceKind.B);
            using H264Picture pastTop = new(sequence, allocator, H264SliceKind.I, H264PictureStructure.TopField);
            using H264Picture pastBottom = new(sequence, allocator, H264SliceKind.I, H264PictureStructure.BottomField);
            using H264Picture futureTop = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.TopField);
            using H264Picture futureBottom = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.BottomField);
            H264ReferencePicture past0 = new(pastTop, 0, new(0, 0, H264PictureStructure.TopField), 0);
            H264ReferencePicture past1 = new(pastBottom, 0, new(0, 1, H264PictureStructure.BottomField), 0)
            {
                FrameIdentity = past0.FrameIdentity
            };
            H264ReferencePicture future0 = new(futureTop, 1, new(4, 0, H264PictureStructure.TopField));
            H264ReferencePicture future1 = new(futureBottom, 1, new(0, 6, H264PictureStructure.BottomField))
            {
                FrameIdentity = future0.FrameIdentity
            };
            futureTop.SetMotion(H264ReferenceList.L0, 0, 0, 32, 32, 0, 4, 1);
            futureBottom.SetMotion(H264ReferenceList.L0, 0, 0, 32, 32, 0, 8, 4);
            futureBottom.SetMotion(H264ReferenceList.L0, 16, 8, 16, 8, 0, 12, -3);
            futureTop.CaptureReferences(0, [past0], []);
            futureBottom.CaptureReferences(0, [past1], []);
            H264BidirectionalReferences references = new([H264ReferencePicture.GetFrame(past0, past1)], [H264ReferencePicture.GetFrame(future0, future1)]);
            Span<byte> prediction = stackalloc byte[384];
            H264DirectPrediction.Predict(target, Header(sequence, H264PictureStructure.Frame), references, new(currentOrder, currentOrder), 16, 16, prediction);
            Assert.Equal(new H264MotionSample(true, 0, motionX, motionY), target.MotionAt(H264ReferenceList.L0, 16, 16));
        }

        private static H264SequenceParameterSet Sequence() => new()
        {
            WidthInMbs = 2,
            HeightInMapUnits = 2,
            Direct8x8Inference = true
        };

        private static H264SliceHeader Header(H264SequenceParameterSet sequence, H264PictureStructure structure) => new(sequence,
            new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 2, 1, false, 0))
        {
            Kind = H264SliceKind.B,
            Structure = structure,
            ActiveReferencesL0 = 2,
            ActiveReferencesL1 = 1
        };
    }
}
