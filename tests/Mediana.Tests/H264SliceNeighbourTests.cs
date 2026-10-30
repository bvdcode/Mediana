// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264SliceNeighbourTests
    {
        [Fact]
        public void SliceStartingInsideARowCannotUseEarlierReconstructedSamples()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = Sequence();
            using (H264Picture picture = new(sequence, allocator, H264SliceKind.P))
            {
                picture.Plane(0).Fill(80);
                picture.Modes.Clear();
                picture.Counts(0).Fill(7);
                picture.SetMotion(H264ReferenceList.L0, 0, 0, 48, 48, 0, 12, 16);
                picture.Modes.Fill(H264Picture.InterMode);
                picture.FirstMacroblock = 4;
                Assert.False(picture.MotionAt(H264ReferenceList.L0, 15, 16).Available);
                Assert.False(picture.MotionAt(H264ReferenceList.L0, 16, 15).Available);
                Assert.True(picture.MotionAt(H264ReferenceList.L0, 19, 16).Available);
                Assert.Equal(0, picture.NeighbourCount(0, 16, 16));
                Assert.Equal(7, picture.NeighbourCount(0, 20, 16));
                byte[] prediction = new byte[256];
                picture.PredictMacroblock(0, 16, 16, 2, prediction);
                Assert.All(prediction, value => Assert.Equal(128, value));
                using H264CabacMacroblockState state = new(sequence, allocator, 4);
                Assert.Equal(-1, state.Left(4));
                Assert.Equal(-1, state.Top(4));
                Assert.Equal(4, state.Left(5));
                Assert.Equal(4, state.Top(7));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ReferenceIndicesAreResolvedWithinTheirOwnSlices()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture first = new(Sequence(), allocator, H264SliceKind.P))
            using (H264Picture second = new(Sequence(), allocator, H264SliceKind.P))
            using (H264Picture picture = new(Sequence(), allocator, H264SliceKind.P))
            {
                picture.CaptureReferences([new(first, 0, new(0, 0))], []);
                picture.FirstMacroblock = 4;
                picture.CaptureReferences([new(second, 1, new(2, 2))], []);
                Assert.Same(first.Identity, picture.ReferenceIdentity(H264ReferenceList.L0, 0, 3));
                Assert.Same(second.Identity, picture.ReferenceIdentity(H264ReferenceList.L0, 0, 4));
                picture.Modes.Fill(H264Picture.InterMode);
                picture.ReferenceIndices(H264ReferenceList.L0).Clear();
                Assert.Equal(1, H264Deblocking.BoundaryStrength(picture, 16, 16, true, true, null));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        public void DeblockingUsesTheCurrentSliceControlAtVerticalAndHorizontalBoundaries(int control, bool vertical)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264SequenceParameterSet sequence = Sequence() with
            {
                WidthInMbs = vertical ? 2 : 1,
                HeightInMapUnits = vertical ? 1 : 2,
                Width = vertical ? 32 : 16,
                Height = vertical ? 16 : 32
            };
            H264PictureParameterSet parameters = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0);
            H264SliceHeader first = new(sequence, parameters);
            H264SliceHeader second = first with { FirstMacroblock = 1, DeblockingIdc = control };
            using (H264Picture picture = new(sequence, allocator, H264SliceKind.I))
            {
                picture.Modes.Fill(2);
                picture.Quantizers.Fill(36);
                for (int y = 0; y < picture.Height; y++)
                {
                    for (int x = 0; x < picture.Width; x++)
                    {
                        picture.Plane(0)[y * picture.Width + x] = (vertical ? x : y) < 16 ? (byte)80 : (byte)100;
                    }
                }
                H264Deblocking.Apply(picture, new H264SliceRegion[] { new(first, 1), new(second, 2) }, default);
                int position = vertical ? 16 : 16 * picture.Width;
                Assert.Equal(control == 0, picture.Plane(0)[position] < 100);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static H264SequenceParameterSet Sequence() => new()
        {
            WidthInMbs = 3,
            HeightInMapUnits = 3,
            FrameMbsOnly = true,
            Width = 48,
            Height = 48
        };
    }
}
