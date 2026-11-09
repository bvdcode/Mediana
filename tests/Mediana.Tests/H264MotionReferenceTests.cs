// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    public class H264MotionReferenceTests
    {
        [Fact]
        public void PreparedReferenceHonorsTheBudgetCancellationAndReuse()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = new(new H264SequenceParameterSet
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true,
                Width = 32,
                Height = 32
            }, allocator, H264SliceKind.P))
            {
                long pictureBytes = allocator.AllocatedBytes;
                MediaBufferAllocator insufficient = allocator.CreateScope(1);
                Assert.Throws<MediaMemoryLimitException>(() => new H264MotionReference(picture, 16, insufficient));
                Assert.Equal(pictureBytes, allocator.AllocatedBytes);
                Assert.Equal(0, insufficient.AllocatedBytes);
                using (H264MotionReference prepared = new(picture, 16, allocator))
                {
                    long preparedBytes = allocator.AllocatedBytes;
                    using CancellationTokenSource cancelled = new();
                    cancelled.Cancel();
                    Assert.Throws<OperationCanceledException>(() => prepared.Prepare(picture, cancelled.Token));
                    for (int index = 0; index < 8; index++)
                    {
                        picture.Plane(0).Fill((byte)(index * 31));
                        prepared.Prepare(picture, default);
                        Assert.False(prepared.Diagonal.ContainsAnyExcept((byte)(index * 31)));
                        Assert.Equal(preparedBytes, allocator.AllocatedBytes);
                    }
                }
                Assert.Equal(pictureBytes, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(16)]
        [InlineData(64)]
        public void ReusedReferencePlanesMatchIndependentConvolutionIncludingBorders(int range)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = new(new H264SequenceParameterSet
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true,
                Width = 32,
                Height = 32
            }, allocator, H264SliceKind.P))
            using (H264MotionReference prepared = new(picture, range, allocator))
            {
                byte[] comparison = new byte[256];
                byte[] output = new byte[262];
                for (int pattern = 0; pattern < 4; pattern++)
                {
                    for (int index = 0; index < 1024; index++)
                    {
                        picture.Plane(0)[index] = pattern switch
                        {
                            0 => (byte)(index * 53 + index / 32 * 97 + index * index % 131),
                            1 => (byte)((index + index / 32) % 2 * 255),
                            2 => (byte)(index < 512 ? 255 : 0),
                            3 => (byte)(index % 32 < 16 ? 0 : 255),
                            _ => throw new InvalidOperationException()
                        };
                    }
                    byte[] original = picture.Plane(0).ToArray();
                    prepared.Prepare(picture, default);
                    foreach (int y in new[] { 1 - range, 0, 8, 16, 15 + range })
                    {
                        foreach (int x in new[] { 1 - range, 0, 8, 16, 15 + range })
                        {
                            H264LumaNeighborhood neighborhood = new(prepared, x, y);
                            for (int dy = -3; dy <= 3; dy++)
                            {
                                for (int dx = -3; dx <= 3; dx++)
                                {
                                    output.AsSpan().Fill(183);
                                    int error = neighborhood.Evaluate(dx, dy, comparison, output.AsSpan(3, 256), int.MaxValue);
                                    int expectedError = 0;
                                    for (int row = 0; row < 16; row++)
                                    {
                                        for (int column = 0; column < 16; column++)
                                        {
                                            int expected = H264MotionInterpolationTestInput.Pixel(original, 4L * (x + column) + dx, 4L * (y + row) + dy);
                                            expectedError += expected;
                                            Assert.Equal(expected, output[3 + row * 16 + column]);
                                        }
                                    }
                                    Assert.Equal(expectedError, error);
                                    Assert.All(output[..3], value => Assert.Equal(183, value));
                                    Assert.All(output[^3..], value => Assert.Equal(183, value));
                                }
                            }
                        }
                    }
                    Assert.True(original.AsSpan().SequenceEqual(picture.Plane(0)));
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
