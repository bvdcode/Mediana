// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264DeblockingTests
    {
        [Fact]
        public void BoundaryStrengthUsesResidualsReferenceIdentityAndQuarterPixelDistance()
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(header.Sequence, allocator, header.Kind);
            using H264Picture first = new(header.Sequence, allocator, header.Kind);
            using H264Picture second = new(header.Sequence, allocator, header.Kind);
            IReadOnlyList<H264ReferencePicture?> references = [new(first, 0, new H264PictureOrder(0, 0)), new(first, 0, new H264PictureOrder(0, 0)), new(second, 1, new H264PictureOrder(2, 2))];
            picture.Modes.Fill(H264Picture.InterMode);
            picture.ReferenceIndices(H264ReferenceList.L0).Clear();
            int index = picture.BlockIndex(0, 16, 0);
            Assert.Equal(0, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            picture.ReferenceIndices(H264ReferenceList.L0)[index] = 1;
            picture.MotionX(H264ReferenceList.L0)[index] = 3;
            picture.MotionY(H264ReferenceList.L0)[index] = -3;
            Assert.Equal(0, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            picture.MotionY(H264ReferenceList.L0)[index] = -4;
            Assert.Equal(1, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            picture.MotionY(H264ReferenceList.L0)[index] = 0;
            picture.ReferenceIndices(H264ReferenceList.L0)[index] = 2;
            Assert.Equal(1, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            picture.Counts(0)[index] = 1;
            Assert.Equal(2, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            picture.Modes[index] = 2;
            Assert.Equal(4, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            Assert.Equal(3, H264Deblocking.BoundaryStrength(picture, 16, 0, true, false, references));
        }

        [Theory]
        [InlineData(false, 84, 96)]
        [InlineData(true, 85, 95)]
        public void InterEdgesUseTheirOwnClippingThreshold(bool residual, byte p0, byte q0)
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(header.Sequence, allocator, header.Kind);
            using H264Picture reference = new(header.Sequence, allocator, header.Kind);
            picture.Modes.Fill(H264Picture.InterMode);
            picture.ReferenceIndices(H264ReferenceList.L0).Clear();
            picture.Quantizers.Fill(36);
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                for (int row = 0; row < size; row++)
                {
                    picture.Plane(plane).Slice(row * size * 2, size).Fill(80);
                    picture.Plane(plane).Slice(row * size * 2 + size, size).Fill(100);
                }
            }
            for (int y = 0; y < 16; y += 4)
            {
                for (int x = 16; x < 32; x += 4)
                {
                    int index = picture.BlockIndex(0, x, y);
                    picture.MotionX(H264ReferenceList.L0)[index] = 4;
                    picture.Counts(0)[index] = residual ? (byte)1 : (byte)0;
                }
            }
            H264Deblocking.Apply(picture, header, default, [new(reference, 0, new H264PictureOrder(0, 0))]);
            for (int row = 0; row < 16; row++)
            {
                Assert.Equal(p0, picture.Plane(0)[row * 32 + 15]);
                Assert.Equal(q0, picture.Plane(0)[row * 32 + 16]);
            }
        }

        [Fact]
        public void EightByEightResidualStrengthIncludesAllFourCavlcGroups()
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(header.Sequence, allocator, header.Kind);
            using H264Picture reference = new(header.Sequence, allocator, header.Kind);
            picture.Modes.Fill(H264Picture.InterMode);
            picture.ReferenceIndices(H264ReferenceList.L0).Clear();
            picture.Counts(0)[picture.BlockIndex(0, 20, 4)] = 1;
            H264ReferencePicture[] references = [new(reference, 0, new H264PictureOrder(0, 0))];
            Assert.Equal(0, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            Assert.Equal(0, H264Deblocking.BoundaryStrength(picture, 16, 8, false, false, references));
            picture.Transforms8x8[1] = 1;
            Assert.Equal(2, H264Deblocking.BoundaryStrength(picture, 16, 0, true, true, references));
            Assert.Equal(2, H264Deblocking.BoundaryStrength(picture, 16, 8, false, false, references));
            Assert.Equal(1, picture.Counts(0)[picture.BlockIndex(0, 20, 4)]);
            Assert.Equal(0, picture.Counts(0)[picture.BlockIndex(0, 16, 0)]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EightByEightTransformSkipsInternalFourPixelLumaEdges(bool transform8x8)
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(header.Sequence, allocator, header.Kind);
            picture.Modes.Fill(2);
            picture.Quantizers.Fill(36);
            picture.Transforms8x8.Fill(transform8x8 ? (byte)1 : (byte)0);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    picture.Plane(0)[y * 32 + x] = x / 4 % 2 == 0 ? (byte)80 : (byte)100;
                }
            }
            H264Deblocking.Apply(picture, header, default);
            Assert.Equal(transform8x8, picture.Plane(0)[3] == 80);
            Assert.Equal(transform8x8, picture.Plane(0)[4] == 100);
        }

        [Theory]
        [InlineData(false, false, 84, 96)]
        [InlineData(false, true, 85, 95)]
        [InlineData(true, false, 84, 96)]
        [InlineData(true, true, 85, 95)]
        public void ChromaEdgesUseLumaStrengthAndIndependentPlaneQuantizers(bool vertical, bool residual, byte p0, byte q0)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = vertical ? 2 : 1,
                HeightInMapUnits = vertical ? 1 : 2,
                FrameMbsOnly = true,
                Width = vertical ? 32 : 16,
                Height = vertical ? 16 : 32
            };
            H264PictureParameterSet parameters = Header().Picture with { ChromaQpOffset = -12, SecondChromaQpOffset = 12 };
            H264SliceHeader header = new(sequence, parameters);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(sequence, allocator, header.Kind);
            using H264Picture reference = new(sequence, allocator, header.Kind);
            picture.Modes.Fill(H264Picture.InterMode);
            picture.ReferenceIndices(H264ReferenceList.L0).Clear();
            picture.Quantizers.Fill(36);
            for (int plane = 0; plane < 3; plane++)
            {
                int stride = picture.Stride(plane);
                int height = picture.Plane(plane).Length / stride;
                int boundary = plane == 0 ? 16 : 8;
                for (int row = 0; row < height; row++)
                {
                    for (int column = 0; column < stride; column++)
                    {
                        picture.Plane(plane)[row * stride + column] = (vertical ? column : row) < boundary ? (byte)80 : (byte)100;
                    }
                }
            }
            for (int y = 0; y < picture.Height; y += 4)
            {
                for (int x = 0; x < picture.Width; x += 4)
                {
                    if ((vertical ? x : y) >= 16)
                    {
                        int index = picture.BlockIndex(0, x, y);
                        picture.MotionX(H264ReferenceList.L0)[index] = 4;
                        picture.Counts(0)[index] = residual ? (byte)1 : (byte)0;
                    }
                }
            }
            H264Deblocking.Apply(picture, header, default, [new(reference, 0, new H264PictureOrder(0, 0))]);
            for (int sample = 0; sample < 8; sample++)
            {
                int stride = picture.Stride(1);
                int previous = vertical ? sample * stride + 7 : 7 * stride + sample;
                int current = previous + (vertical ? 1 : stride);
                Assert.Equal(80, picture.Plane(1)[previous]);
                Assert.Equal(100, picture.Plane(1)[current]);
                Assert.Equal(p0, picture.Plane(2)[previous]);
                Assert.Equal(q0, picture.Plane(2)[current]);
            }
        }

        private static H264SliceHeader Header() => new(new H264SequenceParameterSet
        {
            WidthInMbs = 2,
            HeightInMapUnits = 1,
            FrameMbsOnly = true,
            Width = 32,
            Height = 16
        }, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0));
    }
}
