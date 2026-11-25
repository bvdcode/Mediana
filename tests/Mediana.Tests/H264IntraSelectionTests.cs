// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264IntraSelectionTests
    {
        [Theory]
        [InlineData(8, false)]
        [InlineData(8, true)]
        [InlineData(24, false)]
        public void TopRightGroupsRespectIntraAvailabilityAndPictureEdges(int x, bool constrained)
        {
            using H264Picture picture = new(new H264SequenceParameterSet
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true
            }, new MediaBufferAllocator(65536), H264SliceKind.I);
            picture.Modes.Clear();
            Span<byte> pixels = picture.Plane(0);
            for (int column = 0; column < picture.Width; column++)
            {
                pixels[7 * picture.Width + column] = (byte)column;
            }
            if (x + 8 < picture.Width)
            {
                picture.Modes[picture.BlockIndex(0, x + 8, 7)] = H264Picture.InterMode;
                picture.Modes[picture.BlockIndex(0, x + 12, 7)] = byte.MaxValue;
            }
            Span<byte> top = stackalloc byte[16];
            Span<byte> left = stackalloc byte[8];
            Assert.True(picture.ReadLumaNeighbours(x, 8, 8, top, left, constrained).HasTop);
            int available = constrained || x + 8 == picture.Width ? 8 : 12;
            for (int i = 0; i < top.Length; i++)
            {
                Assert.Equal((byte)(x + (i < available ? i : 7)), top[i]);
            }
        }

        [Fact]
        public void PredictedFirstTraversalPreservesScalarMinimumAndEveryTie()
        {
            MediaBufferAllocator allocator = new(1048576);
            using (H264Picture picture = new(new H264SequenceParameterSet
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true
            }, allocator, H264SliceKind.I))
            {
                Random random = new(71093);
                byte[] input = new byte[64];
                byte[] top = new byte[8], left = new byte[4];
                byte[] candidate = new byte[16], expected = new byte[16], actual = new byte[20];
                for (int predicted = 0; predicted < 9; predicted++)
                {
                    for (int sample = 0; sample < 256; sample++)
                    {
                        picture.Modes.Fill((byte)predicted);
                        random.NextBytes(picture.Plane(0));
                        random.NextBytes(input);
                        if (sample < 9)
                        {
                            picture.ReadLumaNeighbours(8, 8, 4, top, left);
                            H264IntraPrediction.Luma4x4(sample, top, left, picture.Plane(0)[7 * 32 + 7], candidate);
                            for (int row = 0; row < 4; row++)
                            {
                                candidate.AsSpan(row * 4, 4).CopyTo(input.AsSpan(row * 16, 4));
                            }
                        }
                        if (sample == 9)
                        {
                            picture.Plane(0).Fill(128);
                            input.AsSpan().Fill(128);
                        }
                        picture.ReadLumaNeighbours(8, 8, 4, top, left);
                        int minimum = int.MaxValue, expectedMode = -1;
                        for (int mode = 0; mode < 9; mode++)
                        {
                            H264IntraPrediction.Luma4x4(mode, top, left, picture.Plane(0)[7 * 32 + 7], candidate);
                            int difference = 0;
                            for (int row = 0; row < 4; row++)
                            {
                                for (int column = 0; column < 4; column++)
                                {
                                    difference += Math.Abs(input[row * 16 + column] - candidate[row * 4 + column]);
                                }
                            }
                            if (difference < minimum || (difference == minimum && mode == predicted))
                            {
                                minimum = difference;
                                expectedMode = mode;
                                candidate.CopyTo(expected, 0);
                            }
                        }
                        actual.AsSpan().Fill(197);
                        int selected = H264IntraModeSelection.Select(input, picture, 8, 8, actual.AsSpan(2, 16));
                        Assert.Equal(expectedMode, selected);
                        Assert.True(expected.AsSpan().SequenceEqual(actual.AsSpan(2, 16)));
                        Assert.All(actual[..2], value => Assert.Equal(197, value));
                        Assert.All(actual[^2..], value => Assert.Equal(197, value));
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
