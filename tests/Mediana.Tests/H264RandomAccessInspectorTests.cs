using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class H264RandomAccessInspectorTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", 2)]
        [InlineData("avc-tail.mov", 2)]
        [InlineData("avc-cabac-8x8-2.mp4", 1)]
        public async Task RealIdrPicturesMatchContainerCandidatesAndIgnoreFalseKeyFlags(string file, int expected)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(file));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            H264RandomAccessInspector inspector = new(reader.Streams[0]);
            int count = 0;
            for (int sample = reader.Tracks[0].SampleCount - 1; sample >= 0; sample--)
            {
                Mp4SampleInfo info = reader.GetSampleInfo(0, sample);
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, sample);
                using MediaBuffer data = Copy(packet.Data.Span, allocator);
                using MediaPacket wrongFlag = new(0, data, isKeyFrame: !info.IsSync);
                Assert.Equal(info.IsSync, inspector.IsIdr(wrongFlag));
                count += inspector.IsIdr(packet) ? 1 : 0;
            }
            Assert.Equal(expected, count);
        }

        [Fact]
        public async Task InvalidIdrHeadersAndMultipleSlicesAreRejected()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-tail.mov"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            H264RandomAccessInspector inspector = new(reader.Streams[0]);
            foreach (byte[] bytes in new[] { H264AccessUnitTests.Packet([0x65]), H264AccessUnitTests.Packet([0x05, 0x88]),
                H264AccessUnitTests.Packet([0x65, 0x88], [0x65, 0x88]) })
            {
                using MediaBuffer data = Copy(bytes, allocator);
                using MediaPacket packet = new(0, data);
                if (bytes.Length > 8)
                {
                    Assert.Throws<NotSupportedException>(() => inspector.IsIdr(packet));
                }
                else
                {
                    Assert.Throws<InvalidDataException>(() => inspector.IsIdr(packet));
                }
            }
        }

        [Fact]
        public async Task CancellationLimitsAndWrongStreamsAreExplicit()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Throws<NotSupportedException>(() => new H264RandomAccessInspector(reader.Streams[1]));
            Assert.Throws<NotSupportedException>(() => new H264RandomAccessInspector(reader.Streams[0], new() { MaxConfigurationBytes = 1 }));
            H264RandomAccessInspector inspector = new(reader.Streams[0]);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0);
            Assert.Throws<OperationCanceledException>(() => inspector.IsIdr(first, new(true)));
            using MediaBuffer data = Copy(first.Data.Span, allocator);
            using MediaPacket other = new(1, data);
            Assert.Throws<ArgumentException>(() => inspector.IsIdr(other));
            Assert.Throws<NotSupportedException>(() =>
                new H264RandomAccessInspector(reader.Streams[0], new() { MaxNalsPerPacket = 1 }).IsIdr(first));
        }

        private static MediaBuffer Copy(ReadOnlySpan<byte> bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            return writable.Publish();
        }
    }
}
