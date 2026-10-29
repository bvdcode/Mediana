// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264DecoderStateTests
    {
        [Fact]
        public void DelayedPicturesKeepTheirTimingAndDrainInPresentationOrder()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                Send(0, 0);
                Send(1, 6);
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                Send(2, 2);
                Receive(decoder, 0);
                Send(3, 4);
                Receive(decoder, 2);
                decoder.CompleteInput();
                Receive(decoder, 4);
                Receive(decoder, 6);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);

                void Send(int number, int order)
                {
                    using MediaPacket packet = Packet(allocator, H264PcmStream.Picture(number, order, (byte)order), order);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EndMarkersDrainPendingFramesAndStreamEndWaitsForInputCompletion(bool streamEnd)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                using MediaPacket first = Packet(allocator, H264PcmStream.Picture(0, 0, 0), 0);
                using MediaPacket second = Packet(allocator, H264PcmStream.Picture(1, 6, 6), 6);
                using MediaPacket end = Packet(allocator, H264AccessUnitTests.Packet([streamEnd ? (byte)11 : (byte)10]), 0);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(second));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(end));
                Receive(decoder, 0);
                Receive(decoder, 6);
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                if (!streamEnd)
                {
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                }
                decoder.CompleteInput();
                if (!streamEnd)
                {
                    Receive(decoder, 0);
                }
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MemoryManagementResetStartsANewPictureOrderEpoch()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                using MediaPacket first = Packet(allocator, H264PcmStream.Picture(0, 0, 0), 0);
                using MediaPacket reset = Packet(allocator, H264PcmStream.Picture(1, 6, 9, reset: true), 9);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(reset));
                Receive(decoder, 0);
                decoder.CompleteInput();
                Receive(decoder, 9);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InBandConfigurationChangesBeginWithIdrAndResetRestoresTheOriginalConfiguration(bool startsWithIdr)
        {
            (EncodedStreamInfo original, byte[][] packets) = await H264DecoderTests.InputAsync();
            byte[] file = await Mp4TestData.ReadAsync("avc-weighted.mp4");
            int start = Mp4TestData.PayloadOffset(file, "avcC");
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(start - 8)) - 8;
            byte[] configuration = file.AsSpan(start, size).ToArray();
            int sequenceLength = BinaryPrimitives.ReadUInt16BigEndian(configuration.AsSpan(6));
            byte[] sequence = configuration.AsSpan(8, sequenceLength).ToArray();
            int pictureOffset = 8 + sequenceLength + 1;
            int pictureLength = BinaryPrimitives.ReadUInt16BigEndian(configuration.AsSpan(pictureOffset));
            byte[] picture = configuration.AsSpan(pictureOffset + 2, pictureLength).ToArray();
            (_, byte[] slice) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor("avc-weighted.mp4"), startsWithIdr ? 0 : 1);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(original, allocator))
            {
                using MediaPacket first = H264DecoderTests.Packet(allocator, packets[0]);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                using VideoFrame initial = decoder.Receive().Value;
                using MediaPacket parameters = H264DecoderTests.Packet(allocator, H264AccessUnitTests.Packet(sequence, picture));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(parameters));
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                using MediaPacket changed = H264DecoderTests.Packet(allocator, H264AccessUnitTests.Packet(slice));
                if (startsWithIdr)
                {
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(changed));
                    using VideoFrame next = decoder.Receive().Value;
                    Assert.Equal(94, next.Format.Width);
                    Assert.Equal(new SampleAspectRatio(4, 3), next.Format.SampleAspectRatio);
                    byte[] expected = await Mp4TestData.ReadAsync("avc-weighted.sequence.yuv");
                    H264TestInput.VerifyFrame(next, expected.AsSpan(0, 94 * 62 * 3 / 2));
                }
                else
                {
                    Assert.Throws<NotSupportedException>(() => decoder.TrySend(changed));
                    Assert.Throws<InvalidOperationException>(() => decoder.Receive());
                }
                decoder.Reset();
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                using VideoFrame restored = decoder.Receive().Value;
                Assert.Equal(80, restored.Format.Width);
                H264TestInput.VerifyFrame(restored, await Mp4TestData.ReadAsync("avc-tail.frame0.yuv"));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BudgetFailureReleasesInternalStorageWithoutReleasingInput()
        {
            MediaBufferAllocator allocator = new(1024);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                using MediaPacket packet = Packet(allocator, H264PcmStream.Picture(0, 0, 0), 0);
                Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
                Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancelledReceiveReleasesBothPendingOutputAndReferenceStorage()
        {
            MediaBufferAllocator allocator = new(4096);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                using (MediaPacket packet = Packet(allocator, H264PcmStream.Picture(0, 0, 0), 0))
                {
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                }
                Assert.True(allocator.AllocatedBytes > 384);
                Assert.Throws<OperationCanceledException>(() => decoder.Receive(new CancellationToken(true)));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
                decoder.Reset();
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void EndOfStreamRejectsFurtherPacketsUntilReset()
        {
            MediaBufferAllocator allocator = new(4096);
            using (IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(H264PcmStream.Create(), allocator))
            {
                using MediaPacket end = Packet(allocator, H264AccessUnitTests.Packet([11]), 0);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(end));
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(end));
                decoder.Reset();
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(end));
                decoder.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] bytes, int time)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory);
            using MediaBuffer data = writable.Publish();
            return new MediaPacket(0, data, new MediaTimestamp(time, new TimeBase(1, 1)), duration: new MediaDuration(1, new TimeBase(1, 1)));
        }

        private static void Receive(IMediaCodec<MediaPacket, VideoFrame> decoder, int sample)
        {
            using VideoFrame frame = decoder.Receive().Value;
            Assert.All(frame.Data.ToArray(), value => Assert.Equal(sample, value));
            Assert.Equal(new MediaTimestamp(sample, new TimeBase(1, 1)), frame.Timestamp);
            Assert.Equal(new MediaDuration(1, new TimeBase(1, 1)), frame.Duration);
        }
    }
}
