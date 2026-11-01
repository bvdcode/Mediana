// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264MotionCompensationTests
    {
        [Fact]
        public void CompleteBlocksMatchScalarConvolutionAtAllPhasesAndPictureBorders() => H264MotionInterpolationTestInput.Verify();

        [Fact]
        public void CompleteChromaBlocksMatchScalarBilinearSamplingAtEveryPhaseAndBorder() => H264ChromaInterpolationTestInput.Verify();

        [Fact]
        public void AllQuarterPositionsPreserveAnInteriorLinearRamp()
        {
            using H264Picture picture = CreatePicture();
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    picture.Plane(0)[y * 16 + x] = (byte)(8 * x + 4 * y);
                }
            }
            byte[] output = new byte[16];
            for (int dy = 0; dy < 4; dy++)
            {
                for (int dx = 0; dx < 4; dx++)
                {
                    H264MotionCompensation.Luma(picture, 4, 4, dx, dy, 4, 4, output);
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            Assert.Equal(48 + 8 * x + 4 * y + 2 * dx + dy, output[y * 4 + x]);
                        }
                    }
                }
            }
        }

        [Fact]
        public void DiagonalImpulseUsesUnroundedIntermediateSixTapValues()
        {
            using H264Picture picture = CreatePicture();
            picture.Plane(0)[8 * 16 + 8] = 64;
            byte[] expected = [0, 0, 0, 0, 0, 0, 13, 20, 0, 13, 25, 33, 0, 20, 33, 40];
            byte[] output = new byte[1];
            for (int dy = 0; dy < 4; dy++)
            {
                for (int dx = 0; dx < 4; dx++)
                {
                    H264MotionCompensation.Luma(picture, 7, 7, dx, dy, 1, 1, output);
                    Assert.Equal(expected[dy * 4 + dx], output[0]);
                }
            }
        }

        [Fact]
        public void ChromaInterpolatesAtAllEighthPositionsAndKeepsPlanesSeparate()
        {
            using H264Picture picture = CreatePicture();
            picture.Plane(1)[2 * 8 + 2] = 0;
            picture.Plane(1)[2 * 8 + 3] = 64;
            picture.Plane(1)[3 * 8 + 2] = 128;
            picture.Plane(1)[3 * 8 + 3] = 192;
            picture.Plane(2).Fill(211);
            byte[] output = new byte[1];
            for (int dy = 0; dy < 8; dy++)
            {
                for (int dx = 0; dx < 8; dx++)
                {
                    H264MotionCompensation.Chroma(picture, 1, 2, 2, dx, dy, 1, 1, output);
                    Assert.Equal(8 * dx + 16 * dy, output[0]);
                    H264MotionCompensation.Chroma(picture, 2, 2, 2, dx, dy, 1, 1, output);
                    Assert.Equal(211, output[0]);
                }
            }
        }

        [Theory]
        [InlineData(int.MinValue, int.MaxValue)]
        [InlineData(-3, -7)]
        [InlineData(31, 17)]
        public void ExtendsPictureBordersWithoutCoordinateOverflow(int motionX, int motionY)
        {
            using H264Picture picture = CreatePicture();
            picture.Plane(0).Fill(73);
            picture.Plane(1).Fill(94);
            byte[] output = new byte[16];
            H264MotionCompensation.Luma(picture, 0, 0, motionX, motionY, 4, 4, output);
            Assert.All(output, value => Assert.Equal(73, value));
            H264MotionCompensation.Chroma(picture, 1, 0, 0, motionX, motionY, 4, 4, output);
            Assert.All(output, value => Assert.Equal(94, value));
        }

        [Fact]
        public void NegativeFractionUsesThePrecedingIntegerSample()
        {
            using H264Picture picture = CreatePicture();
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    picture.Plane(0)[y * 16 + x] = (byte)(8 * x + 4 * y);
                }
            }
            byte[] output = new byte[1];
            H264MotionCompensation.Luma(picture, 4, 4, -1, -1, 1, 1, output);
            Assert.Equal(45, output[0]);
        }

        [Theory]
        [InlineData(6, 0)]
        [InlineData(8, 255)]
        public void SixTapOvershootIsClippedToTheSampleRange(int x, byte expected)
        {
            using H264Picture picture = CreatePicture();
            for (int y = 0; y < 16; y++)
            {
                picture.Plane(0).Slice(y * 16 + 8, 8).Fill(255);
            }
            byte[] output = new byte[1];
            H264MotionCompensation.Luma(picture, x, 8, 2, 0, 1, 1, output);
            Assert.Equal(expected, output[0]);
        }

        [Theory]
        [InlineData(0, 4)]
        [InlineData(1, 8)]
        [InlineData(2, 8)]
        public void IntegerBlocksMatchClampedSamplesAtEveryBorder(int plane, int units)
        {
            using H264Picture picture = CreatePicture();
            int stride = picture.Stride(plane);
            for (int index = 0; index < picture.Plane(plane).Length; index++)
            {
                picture.Plane(plane)[index] = (byte)((index * 53 + plane * 29) % 251);
            }
            int[] coordinates = [int.MinValue, -40, -16, -8, 0, 8, 16, 40, int.MaxValue - 7];
            byte[] output = new byte[17 * 19];
            foreach (int dx in coordinates)
            {
                foreach (int dy in coordinates)
                {
                    if (plane == 0)
                    {
                        H264MotionCompensation.Luma(picture, 3, 2, dx, dy, 17, 19, output);
                    }
                    else
                    {
                        H264MotionCompensation.Chroma(picture, plane, 3, 2, dx, dy, 17, 19, output);
                    }
                    for (int row = 0; row < 19; row++)
                    {
                        for (int column = 0; column < 17; column++)
                        {
                            int x = (int)Math.Clamp(3L + column + dx / units, 0, stride - 1);
                            int y = (int)Math.Clamp(2L + row + dy / units, 0, stride - 1);
                            Assert.Equal(picture.Plane(plane)[y * stride + x], output[row * 17 + column]);
                        }
                    }
                }
            }
        }

        private static H264Picture CreatePicture() => new(new H264SequenceParameterSet
        {
            WidthInMbs = 1,
            HeightInMapUnits = 1,
            FrameMbsOnly = true,
            Width = 16,
            Height = 16
        }, new MediaBufferAllocator(1024 * 1024), H264SliceKind.P);
    }
}
