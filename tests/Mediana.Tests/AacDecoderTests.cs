using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AacDecoderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputSurvivesDrainResetAndDecoderDisposal(bool stereo)
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            AudioFrame? retained = null;
            try
            {
                using MediaPacket packet = Packet(allocator, stereo ? AacTestPackets.Stereo() : AacTestPackets.Mono());
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(stereo), allocator);
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                decoder.CompleteInput();
                retained = decoder.Receive().Value;
                byte[] expected = retained.GetPlane(0).ToArray();
                Assert.Equal(1024, retained.SamplesPerChannel);
                Assert.Equal(new MediaTimestamp(123, new TimeBase(1, 48000)), retained.Timestamp);
                Assert.Equal(new MediaDuration(1024, new TimeBase(1, 48000)), retained.Duration);
                Assert.Equal(AudioSampleFormat.Float32, retained.Format.SampleFormat);
                Assert.Equal(stereo ? new[] { "front-left", "front-right" } : new[] { "front-center" }, retained.Format.Channels);
                Assert.Equal(packet.Data.Length + expected.Length, allocator.AllocatedBytes);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                decoder.Reset();
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using AudioFrame repeated = decoder.Receive().Value;
                Assert.Equal(expected, repeated.GetPlane(0).ToArray());
                decoder.Dispose();
                Assert.Equal(expected, retained.GetPlane(0).ToArray());
                Assert.Throws<ObjectDisposedException>(() => decoder.Receive());
            }
            finally
            {
                retained?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MalformedPacketFaultsAndResetRestoresInitialOverlap()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Mono());
            using MediaPacket invalid = Packet(allocator, [0xff]);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(false), allocator);
            decoder.TrySend(packet);
            using AudioFrame first = decoder.Receive().Value;
            byte[] expected = first.GetPlane(0).ToArray();
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(invalid));
            Assert.Equal(packet.Data.Length + invalid.Data.Length + expected.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame repeated = decoder.Receive().Value;
            Assert.Equal(expected, repeated.GetPlane(0).ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancellationReleasesWorkspaceAndPendingOutput(bool receive)
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Mono());
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(false), allocator);
            decoder.TrySend(packet);
            using CancellationTokenSource source = new();
            source.Cancel();
            if (receive)
            {
                Assert.Throws<OperationCanceledException>(() => decoder.Receive(source.Token));
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, source.Token));
            }
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.CompleteInput());
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(1024, frame.SamplesPerChannel);
        }

        [Fact]
        public void PartialWorkspaceAllocationFailureDoesNotLeakTheFirstChannel()
        {
            MediaBufferAllocator allocator = new(32768);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Stereo());
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(true), allocator);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            decoder.Reset();
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
        }

        [Fact]
        public void AccessUnitLimitAndUnsupportedSbrAreExplicit()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using MediaPacket packet = Packet(allocator, AacTestPackets.Mono());
            using IMediaCodec<MediaPacket, AudioFrame> limited = new AacDecoderFactory(1).Create(Stream(false), allocator);
            Assert.Throws<NotSupportedException>(() => limited.TrySend(packet));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            byte[] sbr = new AacTestBitWriter().Write(6, 3).Write(1, 4).Write(13, 4).Write(0, 4).Write(7, 3).ToArray();
            using MediaPacket extension = Packet(allocator, sbr);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(false), allocator);
            Assert.Throws<NotSupportedException>(() => decoder.TrySend(extension));
            Assert.Equal(packet.Data.Length + extension.Data.Length, allocator.AllocatedBytes);
        }

        [Fact]
        public void TrailingDataLayoutMismatchAndForbiddenIntensityMaskAreRejected()
        {
            foreach (byte[] invalid in new[] { AacTestPackets.Stereo(), AacTestPackets.Mono().Concat(new byte[] { 0 }).ToArray() })
            {
                MediaBufferAllocator allocator = new(256 * 1024);
                using MediaPacket packet = Packet(allocator, invalid);
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(Stream(false), allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            }
            MediaBufferAllocator stereoAllocator = new(256 * 1024);
            using MediaPacket stereo = Packet(stereoAllocator, AacTestPackets.Stereo(true, 2));
            using IMediaCodec<MediaPacket, AudioFrame> stereoDecoder = new AacDecoderFactory().Create(Stream(true), stereoAllocator);
            Assert.Throws<InvalidDataException>(() => stereoDecoder.TrySend(stereo));
            Assert.Equal(stereo.Data.Length, stereoAllocator.AllocatedBytes);
        }

        private static EncodedStreamInfo Stream(bool stereo) => new(0, MediaKind.Audio, "aac", new TimeBase(1, 48000),
            stereo ? new byte[] { 0x11, 0x90 } : new byte[] { 0x11, 0x88 });

        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] data)
        {
            using WritableMediaBuffer writable = allocator.Allocate(data.Length);
            data.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(123, new TimeBase(1, 48000)));
        }
    }
}
