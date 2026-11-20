// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264FieldReconstructionTests
    {
        [Theory]
        [InlineData("avc-paff-cabac")]
        [InlineData("avc-paff-cavlc")]
        public Task PredictiveFieldsMatchEveryIndependentDecodedSample(string name) =>
            H264FieldTestInput.VerifySequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name);

        [Theory]
        [InlineData("avc-paff-b-cabac")]
        [InlineData("avc-paff-b-cavlc")]
        [InlineData("avc-paff-b-cabac-temporal")]
        [InlineData("avc-paff-b-cavlc-temporal")]
        [InlineData("avc-paff-high10-cabac")]
        [InlineData("avc-paff-high422-cavlc")]
        [InlineData("avc-paff-high422-10-cabac")]
        public Task BidirectionalFieldsMatchEveryIndependentDecodedSample(string name) =>
            H264FieldTestInput.VerifySequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name,
                [0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 11]);

        [Theory]
        [InlineData("avc-paff-cabac", 0)]
        [InlineData("avc-paff-cabac", 8)]
        [InlineData("avc-paff-cabac", 16)]
        [InlineData("avc-paff-cavlc", 0)]
        [InlineData("avc-paff-cavlc", 8)]
        [InlineData("avc-paff-cavlc", 16)]
        public Task IntraFieldsMatchIndependentDecodedSamples(string name, int packet) =>
            H264FieldTestInput.VerifyIntraAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, packet);

        [Theory]
        [InlineData(8, 1)]
        [InlineData(8, 2)]
        [InlineData(10, 1)]
        [InlineData(10, 2)]
        public void FieldStorageUsesHalfTheFrameHeightForBothSamplesAndBlockMetadata(int precision, int chroma)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                LumaBitDepth = precision,
                ChromaBitDepth = precision,
                ChromaFormat = chroma
            };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture frame = new(sequence, allocator, H264SliceKind.P))
            using (H264Picture field = new(sequence, allocator, H264SliceKind.P, H264PictureStructure.BottomField))
            {
                Assert.Same(sequence, field.Sequence);
                Assert.Equal(frame.Width, field.Width);
                Assert.Equal(frame.Height / 2, field.Height);
                Assert.Equal(frame.Quantizers.Length / 2, field.Quantizers.Length);
                Assert.Equal(frame.Modes.Length / 2, field.Modes.Length);
                Assert.Equal(frame.MotionX(H264ReferenceList.L0).Length / 2, field.MotionX(H264ReferenceList.L0).Length);
                for (int plane = 0; plane < 3; plane++)
                {
                    Assert.Equal(frame.Plane(plane).Length / 2, field.Plane(plane).Length);
                    Assert.Equal(frame.Counts(plane).Length / 2, field.Counts(plane).Length);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0, 4, 0)]
        [InlineData(1, 3, 1)]
        [InlineData(2, 3, 1)]
        public void FieldDeblockingUsesWeakerHorizontalIntraEdgesAndFrameSampleMotionUnits(int structureValue,
            int horizontalStrength, int motionStrength)
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 2, HeightInMapUnits = 2 };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(sequence, allocator, H264SliceKind.P, (H264PictureStructure)structureValue);
            using H264Picture reference = new(sequence, allocator, H264SliceKind.P, (H264PictureStructure)structureValue);
            picture.Modes.Fill(2);
            Assert.Equal(4, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, null));
            Assert.Equal(horizontalStrength, H264Deblocking.BoundaryStrength(picture, 0, 16, false, true, null));
            picture.Modes.Fill(H264Picture.InterMode);
            picture.ReferenceIndices(H264ReferenceList.L0).Clear();
            picture.MotionY(H264ReferenceList.L0)[picture.BlockIndex(0, 0, 16)] = 2;
            H264ReferencePicture?[] references = [new(reference, 0, default)];
            Assert.Equal(motionStrength, H264Deblocking.BoundaryStrength(picture, 0, 16, false, true, references));
        }

    }
}
