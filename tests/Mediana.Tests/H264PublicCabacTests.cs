using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264PublicCabacTests
    {
        [Fact]
        public async Task HighProfileIntraDrainsAndTheReturnedFrameSurvivesResetAndDisposal()
        {
            (EncodedStreamInfo stream, byte[][] packets) = await H264DecoderTests.InputAsync("avc-aac.mp4");
            H264DecoderFactory factory = new();
            Assert.Null(factory.GetUnsupportedReason(stream));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
            using MediaPacket packet = H264DecoderTests.Packet(allocator, packets[0]);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            decoder.CompleteInput();
            using VideoFrame frame = decoder.Receive().Value;
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            decoder.Reset();
            decoder.Dispose();
            packet.Dispose();
            H264TestInput.VerifyFrame(frame, await Mp4TestData.ReadAsync("avc-aac.frame0.yuv"));
            Assert.Equal(frame.Data.Length, allocator.AllocatedBytes);
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PredictiveFailureOrCancellationReleasesDelayedStorageAndResetRecovers(bool cancel)
        {
            (EncodedStreamInfo stream, byte[][] packets) = await H264DecoderTests.InputAsync("avc-aac.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(stream, allocator);
            using MediaPacket first = H264DecoderTests.Packet(allocator, packets[0]);
            using MediaPacket second = H264DecoderTests.Packet(allocator, packets[1]);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            if (cancel)
            {
                Assert.Throws<OperationCanceledException>(() => decoder.TrySend(second, new CancellationToken(true)));
            }
            else
            {
                NotSupportedException error = Assert.Throws<NotSupportedException>(() => decoder.TrySend(second));
                Assert.Contains("CABAC P", error.Message);
            }
            Assert.Equal(first.Data.Length + second.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            decoder.CompleteInput();
            using VideoFrame frame = decoder.Receive().Value;
            H264TestInput.VerifyFrame(frame, await Mp4TestData.ReadAsync("avc-aac.frame0.yuv"));
            decoder.Dispose();
            frame.Dispose();
            first.Dispose();
            second.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
