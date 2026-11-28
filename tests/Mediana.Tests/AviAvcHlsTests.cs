// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers;
using Mediana.Containers.Avi;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AviAvcHlsTests
    {
        private const int FramesPerWindow = 16;
        private const int Width = 64;
        private const int Height = 48;
        private const int PictureBytes = Width * Height * 3 / 2;
        private static TimeBase Clock { get; } = new(1, 12);
        private static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "Avi");

        [Theory]
        [InlineData("h264-annexb.avi", "avc.annex-b")]
        [InlineData("h264-avcc.avi", "avc.length-prefixed")]
        public async Task ReorderedAviPlansSeeksAndTranscodesIndependentWindows(string file, string framing)
        {
            H264AnnexBDecoderFactory annexB = new();
            H264DecoderFactory avc = new();
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new AviReaderFactory())
                .AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(annexB)
                .AddVideoDecoder(avc)
                .Build();

            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Directory, file));
            byte[] reference = await ReadReferenceAsync();
            await using TrackingRandomAccessSource source = new(input, 93);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            MediaDuration target = new(FramesPerWindow, Clock);

            await using (ContainerSession container = await engine.OpenContainerAsync(source, allocator, new()))
            {
                EncodedStreamInfo stream = Assert.Single(container.Streams);
                using VideoSegmentPlan plan = container.PlanVideoSegments(stream.StreamIndex, target, allocator);
                Assert.Equal("h264", stream.CodecId);
                Assert.Equal(framing, stream.Format.PacketFraming);
                Assert.Equal(EncodedPacketBoundary.AccessUnit, stream.Format.Boundary);
                Assert.NotNull(new H264StreamMetadataReader().Read(stream));
                Assert.Equal(3, plan.Count);
                for (int number = 0; number < plan.Count; number++)
                {
                    Assert.Equal(new MediaTimestamp(number * FramesPerWindow, Clock), plan[number].Start);
                    Assert.Equal(new MediaTimestamp((number + 1) * FramesPerWindow, Clock), plan[number].End);
                }
            }

            const int PreviewIndex = 14;
            VisualOpenOptions preview = new() { Request = new() { Time = new(PreviewIndex, Clock) } };
            using MemoryStream png = new();
            VisualExportResult result = await engine.ExportPreviewAsync(source, png, new PngImageEncoder(), allocator, new() { Input = preview });
            Assert.Equal(new MediaTimestamp(PreviewIndex, Clock), result.FirstTimestamp);
            Assert.Equal((Width, Height), (result.Width, result.Height));
            Assert.True(png.Length > 0);

            VideoFormat format;
            await using (VisualSession visual = await engine.OpenVisualAsync(source, allocator, preview))
            {
                using VideoFrame picture = Assert.IsType<VideoFrame>(await visual.ReadFrameAsync());
                Assert.Equal(new MediaTimestamp(PreviewIndex, Clock), picture.Timestamp);
                Assert.Equal(0L, PictureError(picture, reference, PreviewIndex));
                format = picture.Format;
            }

            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                Assert.Equal(48, reader.GetPacketCount(0));
                int keyFrames = 0;
                bool reordered = false;
                for (long index = 0; index < reader.GetPacketCount(0); index++)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    if (timing.IsKeyFrame == true)
                    {
                        Assert.Equal(new MediaTimestamp(keyFrames * 12, Clock), timing.PresentationTime);
                        keyFrames++;
                    }
                    reordered |= timing.PresentationTime != timing.DecodeTime;
                }
                Assert.Equal(4, keyFrames);
                Assert.True(reordered);
                source.Reads.Clear();
                using (MediaPacket restart = await reader.ReadPacketAtAsync(0, 12))
                {
                    Assert.True(restart.IsKeyFrame);
                }
                long secondGopStart = source.Reads.Min(read => read.Offset);
                source.Reads.Clear();
                using MemoryStream initialization = new();
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0,
                    framing == "avc.annex-b" ? annexB : avc, format, target, allocator,
                    options: new() { EntropyCoding = H264EntropyCoding.Cabac, Quantizer = 18 });
                await session.WriteInitializationAsync(initialization);
                Assert.Empty(source.Reads);
                foreach (int number in new[] { 1, 0 })
                {
                    source.Reads.Clear();
                    await VerifySegmentAsync(session, number, initialization, engine, allocator, reference);
                    Assert.NotEmpty(source.Reads);
                    if (number == 1)
                    {
                        Assert.All(source.Reads, read => Assert.True(read.Offset >= secondGopStart));
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task VerifySegmentAsync(AvcHlsTranscodeSession session, int number, MemoryStream initialization,
            MediaEngine engine, MediaBufferAllocator allocator, byte[] reference)
        {
            using MemoryStream fragment = new();
            await fragment.WriteAsync(initialization.GetBuffer().AsMemory(0, (int)initialization.Length));
            HlsMediaSegment segment = await session.WriteSegmentAsync(number, fragment);
            int first = number * FramesPerWindow;
            Assert.Equal(new MediaTimestamp(first, Clock), segment.Start);
            Assert.Equal(new MediaDuration(FramesPerWindow, Clock), segment.Duration);
            fragment.Position = 0;
            await using RandomAccessStreamByteSource output = new(fragment, leaveOpen: true);
            await using VideoSession decoded = await engine.OpenVideoAsync(output, allocator, new(), streamIndex: null);
            for (int offset = 0; offset < FramesPerWindow; offset++)
            {
                int index = first + offset;
                using VideoFrame frame = Assert.IsType<VideoFrame>(await decoded.ReadFrameAsync());
                Assert.Equal(new MediaTimestamp(index, Clock), frame.Timestamp);
                Assert.Equal(new MediaDuration(1, Clock), frame.Duration);
                Assert.InRange(PictureError(frame, reference, index), 0, 4L * PictureBytes);
            }
            Assert.Null(await decoded.ReadFrameAsync());
        }

        private static long PictureError(VideoFrame frame, byte[] reference, int index)
        {
            Assert.Equal((Width, Height), (frame.Format.Width, frame.Format.Height));
            Assert.Equal(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
            int offset = index * PictureBytes;
            long error = 0;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    foreach (byte sample in frame.GetRow(plane, row))
                    {
                        error += Math.Abs(sample - reference[offset++]);
                    }
                }
            }
            Assert.Equal((index + 1) * PictureBytes, offset);
            return error;
        }

        private static async ValueTask<byte[]> ReadReferenceAsync()
        {
            await using FileStream file = File.OpenRead(Path.Combine(Directory, "h264-display.yuv.gz"));
            await using GZipStream compressed = new(file, CompressionMode.Decompress);
            byte[] data = new byte[48 * PictureBytes];
            await compressed.ReadExactlyAsync(data);
            return data;
        }
    }
}
