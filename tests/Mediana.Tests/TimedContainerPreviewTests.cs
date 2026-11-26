// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Mpeg2;
using Mediana.Codecs.Mpeg4Visual;
using Mediana.Codecs.Png;
using Mediana.Containers.Asf;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class TimedContainerPreviewTests
    {
        [Theory]
        [InlineData("timed-mpeg2.mpg", 138600, 90000, 1)]
        [InlineData("timed-annexb.ts", 223200, 90000, 0)]
        [InlineData("timed-linear.wmv", 1000, 1000, 0)]
        public async Task ExportPreviewSelectsOneSecondOnThePresentationTimeline(
            string file, int timestamp, int clock, int tolerance)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new MpegPsReaderFactory())
                .AddContainer(new MpegTsReaderFactory())
                .AddContainer(new AsfReaderFactory())
                .AddVideoDecoder(new Mpeg2DecoderFactory())
                .AddVideoDecoder(new H264AnnexBDecoderFactory())
                .AddVideoDecoder(new MicrosoftMpeg4DecoderFactory(MicrosoftMpeg4Configuration.Wmv2CodecId))
                .Build();
            string directory = Path.Combine(AppContext.BaseDirectory, "Data", "ReadSupport");
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(directory, file)));
            using MemoryStream png = new();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            VisualOpenOptions input = new() { Request = new() { Time = new(1, new(1, 1)) } };
            MediaTimestamp expectedTime = new(timestamp, new(1, clock));

            VisualExportResult export = await engine.ExportPreviewAsync(source, png, new PngImageEncoder(), allocator,
                new() { Input = input, ColorResolver = SdrPreviewColorPolicy.Resolve, UnspecifiedSampleAspectRatio = SampleAspectRatio.Square });

            Assert.Equal(expectedTime, export.FirstTimestamp);
            Assert.Equal((1L, 64, 48), (export.Frames, export.Width, export.Height));
            Assert.True(png.GetBuffer().AsSpan(0, (int)png.Length).StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));

            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(directory, Path.ChangeExtension(file, ".yuv")));
            await using (VisualSession session = await engine.OpenVisualAsync(source, allocator, input))
            {
                using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
                Assert.Equal(expectedTime, frame.Timestamp);
                Assert.Equal(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                int offset = 0;
                for (int plane = 0; plane < frame.Planes.Count; plane++)
                {
                    for (int row = 0; row < frame.Planes[plane].Height; row++)
                    {
                        foreach (byte sample in frame.GetRow(plane, row))
                        {
                            Assert.InRange(Math.Abs(sample - reference[offset]), 0, tolerance);
                            offset++;
                        }
                    }
                }
                Assert.Equal(reference.Length, offset);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
