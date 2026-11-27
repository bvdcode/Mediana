// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class JpegPreviewResolutionTests
    {
        private static string Data(string group, string file) => Path.Combine(AppContext.BaseDirectory, "Data", group, file);

        [Theory]
        [InlineData(1, 5, 3)]
        [InlineData(3, 9, 5)]
        [InlineData(5, 9, 5)]
        [InlineData(9, 18, 10)]
        [InlineData(18, 35, 19)]
        [InlineData(int.MaxValue, 35, 19)]
        public async Task ChoosesNativeScaleAndRoundsOddDimensionsUp(int edge, int width, int height)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg", "progressive-rgb-s0-35x19.jpg"));
            using VideoFrame frame = await new JpegImageDecoder().DecodeAsync(jpeg,
                new() { PreferredLongEdge = edge }, new(1024 * 1024));
            Assert.Equal((width, height), (frame.Format.Width, frame.Format.Height));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DisplayAxisHintsRespectRotationEvenWhenExifFollowsTheScan(bool early)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg", "rgb-s0-35x19.jpg"));
            byte[] marker = JpegTestData.Segment(225,
                [.. "Exif\0\0II"u8, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0]);
            int at = early ? 2 : jpeg.Length - 2;
            byte[] tagged = [.. jpeg.AsSpan(0, at), .. marker, .. jpeg.AsSpan(at)];
            MediaBufferAllocator allocator = new(1024 * 1024);
            JpegImageDecoder decoder = new();
            using VideoFrame rotated = await decoder.DecodeAsync(tagged, new() { PreferredWidth = 8, PreferredLongEdge = 9 }, allocator);
            using VideoFrame upright = await decoder.DecodeAsync(jpeg, new() { PreferredLongEdge = 9 }, allocator);
            Assert.Equal((10, 18), (rotated.Format.Width, rotated.Format.Height));
            for (int y = 0; y < 18; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    Assert.True(rotated.Data.Span.Slice((y * 10 + x) * 4, 4)
                        .SequenceEqual(upright.Data.Span.Slice(((9 - x) * 18 + y) * 4, 4)));
                }
            }
        }

        [Fact]
        public async Task ReducedPresentationHonorsSourceLimitsAndReleasesChargedStorage()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg", "rgb-s0-35x19.jpg"));
            MediaBufferAllocator allocator = new(600);
            JpegImageDecoder decoder = new();
            ImageDecodeRequest request = new() { PreferredLongEdge = 1 };
            using (VideoFrame frame = await decoder.DecodeAsync(jpeg, request, allocator))
            {
                Assert.Equal(60, allocator.AllocatedBytes);
            }
            Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(jpeg, new(), allocator));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(jpeg, request with { MaximumPixels = 500 }, allocator));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(jpeg.AsMemory(0, jpeg.Length - 1), request, allocator));
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(jpeg, request, allocator, new(true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode(jpeg, request with { PreferredLongEdge = 0 }, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ReducedProgressiveCmykPreservesProfileAndBlackChannel()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Raster", "cmyk-progressive.jpg"));
            byte[] profile = await File.ReadAllBytesAsync(Data("IccCmyk", "v2-mft2-lab.icc"));
            using VideoFrame reduced = await new JpegImageDecoder().DecodeAsync(JpegCmykIccTestInput.Attach(jpeg, profile),
                new() { PreferredLongEdge = 1 }, new(1024 * 1024));
            Assert.Equal(VideoPixelFormat.Cmyk32, reduced.Format.PixelFormat);
            Assert.Equal(profile, Assert.IsType<IccColorDescription>(reduced.Format.ColorDescription).Profile.ToArray());
            Assert.Contains(Enumerable.Range(0, reduced.Data.Length / 4), i => reduced.Data.Span[i * 4 + 3] != 255);
        }
    }
}
