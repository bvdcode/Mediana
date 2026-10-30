// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AvcHlsValidationTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        public async Task FirstSegmentUsesSliceHeadersInsteadOfTheContainerKeyFlag(int sample, bool idr)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-multislice-cabac.mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using MediaPacket original = await reader.ReadPacketAtAsync(0, sample);
            Assert.Equal(idr, new H264RandomAccessInspector(reader.Streams[0]).IsIdr(original));
            using WritableMediaBuffer storage = allocator.Allocate(original.Data.Length);
            original.Data.CopyTo(storage.Memory);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp time = new(0, reader.Streams[0].TimeBase);
            using MediaPacket packet = new(0, buffer, time, time, new(1, new(1, 6)), isKeyFrame: !idr);
            int opened = 0;
            await using AvcHlsWriter writer = AvcHlsWriter.Create(reader.Streams[0], (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }, allocator);
            if (idr)
            {
                await writer.WritePacketAsync(packet);
                await writer.CompleteAsync();
                Assert.Single(writer.Segments);
                Assert.Equal(1, opened);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(packet).AsTask());
                Assert.Equal(0, opened);
            }
            await writer.DisposeAsync();
            packet.Dispose();
            buffer.Dispose();
            original.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RepeatedPresentationTimeFaultsWithoutAppendingTransportBytes()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame frame = H264EncoderTests.Frame(H264EncoderTests.Format(16, 16), allocator, 0);
            using H264Encoder encoder = H264Encoder.Create(frame.Format, new(1, 1000), allocator);
            encoder.TrySend(frame);
            using MediaPacket packet = encoder.Receive().Value!;
            HlsTestDestination destination = new();
            await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, (_, _) => ValueTask.FromResult<Stream>(destination), allocator);
            await writer.WritePacketAsync(packet);
            long bytes = writer.BytesWritten;
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(packet).AsTask());
            Assert.Equal(bytes, writer.BytesWritten);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            await writer.DisposeAsync();
            Assert.True(destination.Disposed);
        }
    }
}
