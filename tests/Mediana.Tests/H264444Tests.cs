// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class H264444Tests
    {
        [Theory]
        [InlineData("cavlc-i")]
        [InlineData("cabac-i")]
        [InlineData("cavlc-b")]
        [InlineData("cabac-b")]
        [InlineData("cabac-q45")]
        public Task EightBitFramesAndReverseSeeksMatchIndependentDecoder(string coding) =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor($"avc-444-8-{coding}"), VideoPixelFormat.Yuv444P8, 83, 75, 18, 12);

        [Theory]
        [InlineData("cavlc-i")]
        [InlineData("cabac-i")]
        [InlineData("cavlc-b")]
        [InlineData("cabac-b")]
        [InlineData("cabac-q45")]
        public Task TenBitFramesAndReverseSeeksMatchIndependentDecoder(string coding) =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor($"avc-444-10-{coding}"), VideoPixelFormat.Yuv444P10, 83, 75, 18, 12);

        [Fact]
        public Task RgbPlanesAndReverseSeeksMatchIndependentDecoder() =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor("avc-444-rgb"), VideoPixelFormat.GbrP8, 83, 75, 18, 12);

        [Fact]
        public async Task RgbVideoConvertsToSrgbWithExactChannelOrder()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("avc-444-rgb.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing RGB reference frame.");
            Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame converted = SrgbConverter.Apply(frame, allocator))
            {
                for (int y = 0; y < frame.Format.Height; y++)
                {
                    for (int x = 0; x < frame.Format.Width; x++)
                    {
                        Assert.Equal(frame.GetRow(2, y)[x], converted.GetRow(0, y)[x * 4]);
                        Assert.Equal(frame.GetRow(0, y)[x], converted.GetRow(0, y)[x * 4 + 1]);
                        Assert.Equal(frame.GetRow(1, y)[x], converted.GetRow(0, y)[x * 4 + 2]);
                        Assert.Equal(255, converted.GetRow(0, y)[x * 4 + 3]);
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        public Task AdaptiveFieldFramesAndReverseSeeksMatchIndependentDecoder(int depth)
        {
            VideoPixelFormat pixels = depth switch
            {
                8 => VideoPixelFormat.Yuv444P8,
                10 => VideoPixelFormat.Yuv444P10,
                _ => throw new ArgumentOutOfRangeException(nameof(depth))
            };
            return H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor($"avc-444-{depth}-mbaff"), pixels, 83, 76, 18, 12);
        }
    }
}
