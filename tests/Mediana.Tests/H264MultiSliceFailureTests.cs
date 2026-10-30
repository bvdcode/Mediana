// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264MultiSliceFailureTests
    {
        [Theory]
        [InlineData("avc-multislice-cavlc")]
        [InlineData("avc-multislice-cabac")]
        public async Task MissingOverlappingReorderedTruncatedAndMixedPicturesFaultWithoutRetainingStorage(string name)
        {
            MediaBufferAllocator sourceAllocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(name + ".mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, sourceAllocator);
            H264DecoderOptions options = new();
            H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, options);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0);
            using MediaPacket second = await reader.ReadPacketAtAsync(0, 1);
            byte[][] slices = H264AccessUnitReader.Read(first.Data, configuration, options).Slices.Select(nal => nal.ToArray()).ToArray();
            byte[][] other = H264AccessUnitReader.Read(second.Data, configuration, options).Slices.Select(nal => nal.ToArray()).ToArray();
            byte[][] malformed =
            [
                H264AccessUnitTests.Packet(slices[0], slices[2], slices[3], slices[4]),
                H264AccessUnitTests.Packet(slices[..4]),
                H264AccessUnitTests.Packet(slices[0], slices[0]),
                H264AccessUnitTests.Packet(slices[0], slices[2], slices[1], slices[3], slices[4]),
                H264AccessUnitTests.Packet(slices[1..]),
                H264AccessUnitTests.Packet(slices[0], slices[1], slices[2], slices[3], slices[4][..^1]),
                H264AccessUnitTests.Packet(slices[0], other[1], other[2], other[3], other[4])
            ];
            foreach (byte[] bytes in malformed)
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                using IMediaCodec<MediaPacket, VideoFrame> decoder =
                    new H264DecoderFactory().Create(reader.Streams[0], allocator);
                using (MediaPacket packet = H264DecoderTests.Packet(sourceAllocator, bytes))
                {
                    Exception? error = Record.Exception(() => decoder.TrySend(packet));
                    Assert.True(error is InvalidDataException or NotSupportedException, error?.ToString());
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
                decoder.Reset();
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
                decoder.CompleteInput();
                using (VideoFrame frame = decoder.Receive().Value)
                {
                    Assert.NotNull(frame);
                }
                decoder.Dispose();
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task CancellationAndAllocationFailureReleaseAllPictureAndSliceStorage()
        {
            MediaBufferAllocator packetAllocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, packetAllocator);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 0);
            H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, new());
            long pictureBytes;
            MediaBufferAllocator measure = new(1024 * 1024);
            using (H264Picture picture = new(configuration.Sequences[0], measure, H264SliceKind.P))
            {
                pictureBytes = measure.AllocatedBytes;
            }
            H264SequenceParameterSet sequence = configuration.Sequences[0];
            long workerBytes = sequence.WidthInMbs * sequence.HeightInMapUnits * 4;
            foreach (long budget in new[] { 1, pictureBytes, pictureBytes + workerBytes - 1 })
            {
                MediaBufferAllocator allocator = new(budget);
                using IMediaCodec<MediaPacket, VideoFrame> decoder = new H264DecoderFactory().Create(reader.Streams[0], allocator);
                Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            MediaBufferAllocator cancelled = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> cancelledDecoder = new H264DecoderFactory().Create(reader.Streams[0], cancelled);
            Assert.Throws<OperationCanceledException>(() => cancelledDecoder.TrySend(packet, new(true)));
            Assert.Equal(0, cancelled.AllocatedBytes);
        }
    }
}
