// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Avi;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Tests;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class MicrosoftMpeg4Tests
    {
        [Theory]
        [InlineData("mp43", MicrosoftMpeg4Configuration.Version3CodecId, 54, 38, 12)]
        [InlineData("wmv2", MicrosoftMpeg4Configuration.Wmv2CodecId, 54, 38, 12)]
        [InlineData("wmv2-loop", MicrosoftMpeg4Configuration.Wmv2CodecId, 54, 38, 12)]
        [InlineData("wmv2-tools", MicrosoftMpeg4Configuration.Wmv2CodecId, 16, 16, 3)]
        public async Task DecodesAndSeeksInAvi(string name, string codecId, int width, int height, int frameCount)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Microsoft");
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new MicrosoftMpeg4DecoderFactory(codecId))
                .Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".avi")));
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                Assert.Equal(codecId, session.Stream.CodecId);
                Assert.True(session.CanSeek);
                int frameSize = width * height * 3 / 2;
                int count = 0;
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        Assert.Equal(width, frame.Format.Width);
                        Assert.Equal(height, frame.Format.Height);
                        Assert.Equal(new MediaTimestamp(count, new(1, 12)), frame.Timestamp);
                        Mpeg4TestInput.VerifyPixels(frame, reference.AsSpan(count * frameSize, frameSize));
                        count++;
                    }
                }
                Assert.Equal(frameCount, count);
                Assert.Equal(count * frameSize, reference.Length);

                foreach (int index in new[] { frameCount - 1, 0, frameCount * 2 / 3, frameCount / 3 })
                {
                    MediaTimestamp timestamp = new(index, new(1, 12));
                    await session.SeekAsync(timestamp);
                    using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAtOrAfterAsync(timestamp));
                    Assert.Equal(timestamp, frame.Timestamp);
                    Mpeg4TestInput.VerifyPixels(frame, reference.AsSpan(index * frameSize, frameSize));
                    using VideoFrame preview = SrgbPreview.FitWithColorPolicy(frame, SdrPreviewColorPolicy.Resolve,
                        VideoDisplayMatrix.Identity, width, height, allocator);
                    Assert.Equal(VideoPixelFormat.Rgba32, preview.Format.PixelFormat);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task Wmv2RepeatPictureUsesTheReferenceStorageWithASeparateTimestamp()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Microsoft", "wmv2-tools.avi");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using AviReader avi = await AviReader.OpenAsync(source, allocator);
            MicrosoftMpeg4DecoderFactory factory = new(MicrosoftMpeg4Configuration.Wmv2CodecId);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(avi.Streams[0], allocator);
            using MediaPacket firstPacket = await avi.ReadPacketAtAsync(0, 0);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(firstPacket));
            using VideoFrame firstFrame = decoder.Receive().Value;

            using WritableMediaBuffer storage = allocator.Allocate(2);
            storage.Span[0] = 0b10010110;
            storage.Span[1] = 0b10000000;
            using MediaBuffer payload = storage.Publish();
            MediaTimestamp time = new(1, new(1, 12));
            using MediaPacket repeatPacket = new(0, payload, time);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(repeatPacket));
            using VideoFrame repeated = decoder.Receive().Value;
            Assert.Equal(time, repeated.Timestamp);
            Assert.True(firstFrame.Data.Equals(repeated.Data));
        }
    }
}
