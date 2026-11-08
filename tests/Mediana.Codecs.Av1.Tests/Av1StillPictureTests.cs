// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1StillPictureTests
    {
        [Theory]
        [InlineData("flat", 16, 16)]
        [InlineData("palette", 16, 16)]
        [InlineData("black", 16, 16)]
        [InlineData("white", 16, 16)]
        [InlineData("solid200", 16, 16)]
        [InlineData("noise16", 16, 16)]
        [InlineData("gradient16", 16, 16)]
        [InlineData("noise64", 64, 64)]
        [InlineData("ramp37x29", 37, 29)]
        [InlineData("shapes96x80", 96, 80)]
        [InlineData("palette48x40", 48, 40)]
        public void ReconstructsIndependentLosslessPixels(string name, int width, int height)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".gray"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1Picture picture = new Av1PictureDecoder(allocator).Decode(encoded))
            {
                Assert.Equal(width, picture.Sequence.Width);
                Assert.Equal(height, picture.Sequence.Height);
                Assert.Equal(width * height, expected.Length);
                for (int row = 0; row < height; row++)
                {
                    ReadOnlySpan<byte> actual = picture.Pixels.Memory.Span.Slice(row * picture.Sequence.Stride, width);
                    Assert.True(expected.AsSpan(row * width, width).SequenceEqual(actual), $"Pixel row {row} differs.");
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
