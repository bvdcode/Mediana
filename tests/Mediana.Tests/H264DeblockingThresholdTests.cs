// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264DeblockingThresholdTests
    {
        [Theory]
        [InlineData(-12, -12)]
        [InlineData(-12, 0)]
        [InlineData(-12, 12)]
        [InlineData(0, -12)]
        [InlineData(0, 0)]
        [InlineData(0, 12)]
        [InlineData(12, -12)]
        [InlineData(12, 0)]
        [InlineData(12, 12)]
        public void ZeroThresholdExclusionPreservesPlaneOffsetsAndNeighbourAverages(int alpha, int beta)
        {
            H264SequenceParameterSet sequence = new()
            {
                WidthInMbs = 2,
                HeightInMapUnits = 1,
                FrameMbsOnly = true,
                Width = 32,
                Height = 16
            };
            H264PictureParameterSet parameters = new(0, 0, false, false, 26, -12, 12, true, false, false, false, 1, 1, false, 0);
            H264SliceHeader header = new(sequence, parameters) { AlphaOffset = alpha, BetaOffset = beta };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture picture = new(sequence, allocator, header.Kind);
            picture.Modes.Fill(2);
            for (int previous = 0; previous <= 51; previous++)
            {
                foreach (int current in new[] { previous, 0, 15, 16, 51 })
                {
                    picture.Quantizers[0] = (byte)previous;
                    picture.Quantizers[1] = (byte)current;
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int stride = picture.Stride(plane);
                        int boundary = stride / 2;
                        for (int row = 0; row < picture.Plane(plane).Length / stride; row++)
                        {
                            picture.Plane(plane).Slice(row * stride, boundary).Fill(100);
                            picture.Plane(plane).Slice(row * stride + boundary, boundary).Fill(102);
                        }
                    }
                    H264Deblocking.Apply(picture, header, default);
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int offset = plane switch { 0 => 0, 1 => -12, 2 => 12, _ => throw new InvalidOperationException() };
                        int firstQp = plane == 0 ? previous : ChromaQp(previous, offset);
                        int secondQp = plane == 0 ? current : ChromaQp(current, offset);
                        int average = (firstQp + secondQp + 1) / 2;
                        // H.264 Tables 8-16/8-17 first permit filtering this two-code-value edge at index 16.
                        bool filtered = average + alpha >= 16 && average + beta >= 16;
                        int boundary = picture.Stride(plane) / 2;
                        Assert.Equal(filtered ? 101 : 100, picture.Plane(plane)[boundary - 1]);
                        Assert.Equal(filtered && plane == 0 ? 101 : 102, picture.Plane(plane)[boundary]);
                    }
                }
            }
        }

        private static int ChromaQp(int luma, int offset)
        {
            ReadOnlySpan<int> values = [29, 30, 31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38, 39, 39, 39, 39];
            int index = Math.Clamp(luma + offset, 0, 51);
            return index < 30 ? index : values[index - 30];
        }
    }
}
