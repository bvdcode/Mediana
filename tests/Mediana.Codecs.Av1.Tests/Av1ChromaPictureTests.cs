// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ChromaPictureTests
    {
        [Theory]
        [InlineData("yuv16", 16, 16)]
        [InlineData("yuvNoise32", 32, 32)]
        [InlineData("yuvShape37x29", 37, 29)]
        [InlineData("yuvCfl64", 64, 64)]
        [InlineData("quantized4", 48, 40)]
        [InlineData("quantizedChroma", 48, 40)]
        [InlineData("quantizedNoise4", 37, 29)]
        [InlineData("quantizedNoise12", 37, 29)]
        [InlineData("quantizedNoise30", 37, 29)]
        [InlineData("quantizedNoise50", 37, 29)]
        public void ReconstructsEveryIndependent420Plane(string name, int width, int height)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".yuv"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1Picture picture = new Av1PictureDecoder(allocator).Decode(encoded))
            {
                Assert.False(picture.Sequence.Monochrome);
                Assert.Equal(width, picture.Sequence.Width);
                Assert.Equal(height, picture.Sequence.Height);
                int sourceOffset = 0;
                for (int plane = 0; plane < 3; plane++)
                {
                    int shift = picture.Sequence.PlaneShift(plane);
                    int planeWidth = (width + (1 << shift) - 1) >> shift;
                    int planeHeight = (height + (1 << shift) - 1) >> shift;
                    int offset = picture.Sequence.PlaneOffset(plane);
                    int stride = picture.Sequence.PlaneStride(plane);
                    for (int row = 0; row < planeHeight; row++)
                    {
                        ReadOnlySpan<byte> actual = picture.Pixels.Memory.Span.Slice(offset + row * stride, planeWidth);
                        Assert.True(expected.AsSpan(sourceOffset, planeWidth).SequenceEqual(actual), $"Plane {plane}, row {row} differs.");
                        sourceOffset += planeWidth;
                    }
                }
                Assert.Equal(expected.Length, sourceOffset);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
