using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MpegTsSegmentStateTests
    {
        [Fact]
        public async Task SnapshotOutlivesOriginalReaderWriterSourceAndAllocator()
        {
            MediaBufferAllocator firstAllocator = new(1024 * 1024);
            MpegTsSegmentState state;
            using (MemoryStream output = new())
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Input())))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, firstAllocator))
            await using (MpegTsWriter writer = MpegTsWriter.Create(output, reader.Streams, firstAllocator))
            using (MediaPacket packet = await reader.ReadPacketAtAsync(0, 0))
            {
                await writer.WritePacketAsync(packet);
                state = writer.CaptureSegmentState();
            }
            Assert.Equal(0, firstAllocator.AllocatedBytes);
            MediaBufferAllocator secondAllocator = new(1024 * 1024);
            using (MemoryStream output = new())
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Input())))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, secondAllocator))
            await using (MpegTsWriter writer = MpegTsWriter.ResumeSegment(output, state, secondAllocator))
            using (MediaPacket packet = await reader.ReadPacketAtAsync(0, 0))
            using (MediaPacket shifted = Shift(secondAllocator, packet, new(1, new TimeBase(1, 1))))
            {
                await writer.WritePacketAsync(shifted);
                await writer.CompleteAsync();
                Assert.NotEqual(0, output.Length);
            }
            Assert.Equal(0, firstAllocator.AllocatedBytes);
            Assert.Equal(0, secondAllocator.AllocatedBytes);
        }

        [Fact]
        public async Task SnapshotRequiresPrecedingOutputWithoutFaultingTheWriter()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream output = new();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Input()));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long readerBytes = allocator.AllocatedBytes;
            await using MpegTsWriter writer = MpegTsWriter.Create(output, reader.Streams, allocator);
            Assert.Throws<InvalidOperationException>(writer.CaptureSegmentState);
            using (MediaPacket packet = await reader.ReadPacketAtAsync(0, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            Assert.NotNull(writer.CaptureSegmentState());
            await writer.CompleteAsync();
            Assert.Throws<InvalidOperationException>(writer.CaptureSegmentState);
            Assert.Equal(readerBytes, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RestoredStateKeepsCustomClockOffsetAndTimestampGapLimit()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream original = new();
            using MemoryStream resumed = new();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Input()));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long readerBytes = allocator.AllocatedBytes;
            MpegTsWriteOptions options = new()
            {
                TimestampOffset = new(7, new TimeBase(1, 1)),
                MaximumTimestampGap = new(1, new TimeBase(1, 2))
            };
            MpegTsSegmentState state;
            using MediaPacket idr = await reader.ReadPacketAtAsync(0, 0);
            await using (MpegTsWriter writer = MpegTsWriter.Create(original, reader.Streams, allocator, options))
            {
                await writer.WritePacketAsync(idr);
                state = writer.CaptureSegmentState();
                await writer.CompleteAsync();
            }
            await using (MpegTsWriter writer = MpegTsWriter.ResumeSegment(resumed, state, allocator))
            using (MediaPacket shifted = Shift(allocator, idr, new(1, new TimeBase(1, 4))))
            {
                await writer.WritePacketAsync(shifted);
                await writer.CompleteAsync();
            }
            byte[] data = resumed.ToArray();
            MpegTsTestInput.Verify(data, reader.Streams,
                [(0, idr.Data.ToArray(), Ticks(idr.PresentationTime) + 652500, Ticks(idr.DecodeTime) + 652500)]);
            using MemoryStream rejected = new();
            await using (MpegTsWriter writer = MpegTsWriter.ResumeSegment(rejected, state, allocator))
            using (MediaPacket shifted = Shift(allocator, idr, new(1, new TimeBase(1, 1))))
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(shifted).AsTask());
            }
            Assert.Equal(0, rejected.Length);
            Assert.Equal(readerBytes + idr.Data.Length, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RejectedResumeDoesNotChangeReusableSnapshot()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream original = new();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Input()));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long readerBytes = allocator.AllocatedBytes;
            MpegTsSegmentState state;
            using MediaPacket idr = await reader.ReadPacketAtAsync(0, 0);
            await using (MpegTsWriter writer = MpegTsWriter.Create(original, reader.Streams, allocator))
            {
                await writer.WritePacketAsync(idr);
                state = writer.CaptureSegmentState();
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using MemoryStream output = new();
                await using MpegTsWriter writer = MpegTsWriter.ResumeSegment(output, state, allocator);
                if (attempt == 0)
                {
                    await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(idr).AsTask());
                    Assert.Equal(0, output.Length);
                }
                else
                {
                    using MediaPacket shifted = Shift(allocator, idr, new(1, new TimeBase(1, 1)));
                    await writer.WritePacketAsync(shifted);
                    await writer.CompleteAsync();
                    Assert.NotEqual(0, output.Length);
                }
            }
            Assert.Equal(readerBytes + idr.Data.Length, allocator.AllocatedBytes);
        }

        private static MediaPacket Shift(MediaBufferAllocator allocator, MediaPacket packet, MediaDuration offset)
        {
            MediaTimestamp pts = packet.PresentationTime!.Value;
            MediaTimestamp dts = packet.DecodeTime!.Value;
            long delta = new MediaTimestamp(offset.Value, offset.TimeBase).Rescale(pts.TimeBase, TimestampRounding.NearestEven).Value;
            using WritableMediaBuffer storage = allocator.Allocate(packet.Data.Length);
            packet.Data.CopyTo(storage.Memory);
            using MediaBuffer buffer = storage.Publish();
            return new(packet.StreamIndex, buffer, new(pts.Value + delta, pts.TimeBase), new(dts.Value + delta, dts.TimeBase));
        }

        private static long Ticks(MediaTimestamp? timestamp) => timestamp!.Value.Rescale(new(1, 90000), TimestampRounding.NearestEven).Value;

        private static string Input() => Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-segments.mp4");
    }
}
