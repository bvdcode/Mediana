using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class H264DecoderTests
    {
        [Theory]
        [InlineData("avc-tail.mov", 6, 80, 48, 5, 1, 1)]
        [InlineData("avc-cropped.mp4", 1, 78, 46, 1, 1, 1)]
        [InlineData("avc-weighted.mp4", 12, 94, 62, 10, 4, 3)]
        public async Task PublicEngineDecodesEveryIndependentSampleAndPreservesDisplayMetadata(string file, int count,
            int width, int height, int fps, int aspectWidth, int aspectHeight)
        {
            MediaEngine engine = Engine();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor(file)));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            byte[]? sequence = file == "avc-weighted.mp4" ? await Mp4TestData.ReadAsync("avc-weighted.sequence.yuv") : null;
            int frameBytes = width * height * 3 / 2;
            for (int index = 0; index < count; index++)
            {
                using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
                Assert.Equal(width, frame.Format.Width);
                Assert.Equal(height, frame.Format.Height);
                Assert.Equal(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                Assert.Equal(new SampleAspectRatio(aspectWidth, aspectHeight), frame.Format.SampleAspectRatio);
                Assert.Equal(ColorRange.Limited, frame.Format.Color.Range);
                Assert.Equal(ChromaLocation.Left, frame.Format.Color.ChromaLocation);
                if (file == "avc-weighted.mp4")
                {
                    Assert.Equal(ColorPrimaries.Bt709, frame.Format.Color.Primaries);
                    Assert.Equal(TransferFunction.Bt709, frame.Format.Color.Transfer);
                    Assert.Equal(MatrixCoefficients.Bt709, frame.Format.Color.Matrix);
                }
                MediaTimestamp timestamp = Assert.IsType<MediaTimestamp>(frame.Timestamp);
                Assert.Equal(0, timestamp.CompareTo(new MediaTimestamp(index, new TimeBase(1, fps))));
                MediaDuration duration = Assert.IsType<MediaDuration>(frame.Duration);
                Assert.Equal(0, new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(1, new TimeBase(1, fps))));
                byte[] expected = sequence is null
                    ? await Mp4TestData.ReadAsync($"{Path.GetFileNameWithoutExtension(file)}.frame{index}.yuv")
                    : sequence.AsSpan(index * frameBytes, frameBytes).ToArray();
                H264TestInput.VerifyFrame(frame, expected);
            }
            Assert.Null(await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task SelectsARealPFrameByPresentationTimeAndKeepsItAfterSessionDisposal()
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("avc-tail.mov")));
            VideoSession session = await Engine().OpenVideoAsync(source, 0);
            using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(3, new TimeBase(1, 5))));
            await session.DisposeAsync();
            Assert.Equal(80 * 48 * 3 / 2, session.AllocatedBufferBytes);
            H264TestInput.VerifyFrame(frame, await Mp4TestData.ReadAsync("avc-tail.frame3.yuv"));
            frame.Dispose();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task BackpressureBorrowsInputAndResetRequiresIdrWhilePreservingReturnedFrames()
        {
            (EncodedStreamInfo stream, byte[][] packets) = await InputAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(stream, allocator);
            using MediaPacket first = Packet(allocator, packets[0]);
            using MediaPacket second = Packet(allocator, packets[1]);
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(second));
            using VideoFrame output = decoder.Receive().Value;
            decoder.Reset();
            H264TestInput.VerifyFrame(output, await Mp4TestData.ReadAsync("avc-tail.frame0.yuv"));
            Assert.Throws<NotSupportedException>(() => decoder.TrySend(second));
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            decoder.CompleteInput();
            decoder.CompleteInput();
            using VideoFrame repeated = decoder.Receive().Value;
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            Assert.Equal(output.Data.ToArray(), repeated.Data.ToArray());
            decoder.Dispose();
            first.Dispose();
            second.Dispose();
            output.Dispose();
            repeated.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailureOrCancellationReleasesReferencesAndResetRecovers(bool cancel)
        {
            (EncodedStreamInfo stream, byte[][] packets) = await InputAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(stream, allocator);
            using MediaPacket packet = Packet(allocator, packets[0]);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            using VideoFrame output = decoder.Receive().Value;
            using MediaPacket damaged = Packet(allocator, [0, 0, 0, 3, 1]);
            Exception? error = Record.Exception(() => decoder.TrySend(damaged, new CancellationToken(cancel)));
            Assert.True(cancel ? error is OperationCanceledException : error is InvalidDataException);
            Assert.Equal(packet.Data.Length + damaged.Data.Length + output.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            decoder.Reset();
            H264TestInput.VerifyFrame(output, await Mp4TestData.ReadAsync("avc-tail.frame0.yuv"));
            packet.Dispose();
            damaged.Dispose();
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnsupportedCabacConfigurationIsRejectedBeforeOpeningTheDecodeSession()
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("avc-aac.mp4")));
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(async () => await Engine().OpenVideoAsync(source, 0));
            Assert.Contains("CAVLC", error.Message);
        }

        internal static async Task<(EncodedStreamInfo Stream, byte[][] Packets)> InputAsync()
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-tail.mov"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            List<byte[]> packets = [];
            while (true)
            {
                using MediaPacket? packet = await reader.ReadPacketAsync();
                if (packet is null)
                {
                    break;
                }
                packets.Add(packet.Data.ToArray());
            }
            return (reader.Streams[0], packets.ToArray());
        }

        internal static MediaPacket Packet(MediaBufferAllocator allocator, byte[] bytes)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
            .AddVideoDecoder(new H264DecoderFactory()).Build();
    }
}
