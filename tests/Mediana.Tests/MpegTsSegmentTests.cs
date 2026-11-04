// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

using Mediana.Formats;

namespace Mediana.Tests
{
    public class MpegTsSegmentTests
    {
        [Fact]
        public async Task RealAvcAacSegmentsPreserveAllPacketsClocksAndCountersAcrossFiles() =>
            await MpegTsSegmentTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task RealAudioSegmentsPreserveAllPacketsAndRestoreOutOfOrder(string file) =>
            await MpegTsAudioSegmentTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SegmentRejectsAudioOrNonIdrBeforeWritingAnyBytes(bool audio)
        {
            MediaBufferAllocator allocator = new(4096);
            using MemoryStream first = new();
            using MemoryStream second = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(first, [Video(), Audio()], allocator);
            using (MediaPacket packet = Packet(allocator, 0, true, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            await writer.BeginSegmentAsync(second);
            using (MediaPacket packet = Packet(allocator, audio ? 1 : 0, false, 1000))
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(packet).AsTask());
            }
            Assert.Equal(0, second.Length);
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task PendingSegmentCannotBeCompletedOrSkipped(bool complete, bool audio)
        {
            MediaBufferAllocator allocator = new(4096);
            using MemoryStream first = new();
            using MemoryStream second = new();
            using MemoryStream third = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(first, audio ? [Audio()] : [Video()], allocator);
            using (MediaPacket packet = Packet(allocator, audio ? 1 : 0, true, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            await writer.BeginSegmentAsync(second);
            if (complete)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => writer.BeginSegmentAsync(third).AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, second.Length);
            Assert.True(first.CanWrite && second.CanWrite && third.CanWrite);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SegmentFlushFailureOrCancellationFaultsWriterAndBorrowsBothStreams(bool cancel)
        {
            MediaBufferAllocator allocator = new(4096);
            using MpegTsTestOutput first = new() { FailOnFlush = !cancel };
            using MemoryStream second = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(first, [Video()], allocator);
            using (MediaPacket packet = Packet(allocator, 0, true, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            if (cancel)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => writer.BeginSegmentAsync(second, new(true)).AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => writer.BeginSegmentAsync(second).AsTask());
            }
            Assert.Equal(0, second.Length);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(first.WasDisposed);
            Assert.True(second.CanWrite);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
        }

        [Fact]
        public async Task FailureInNewSegmentDiscardsWriterStateAndKeepsOutputsBorrowed()
        {
            MediaBufferAllocator allocator = new(4096);
            using MemoryStream first = new();
            using MpegTsTestOutput second = new() { FailOnWrite = 2 };
            await using MpegTsWriter writer = MpegTsWriter.Create(first, [Video()], allocator);
            using (MediaPacket packet = Packet(allocator, 0, true, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            await writer.BeginSegmentAsync(second);
            using (MediaPacket packet = Packet(allocator, 0, true, 1000))
            {
                await Assert.ThrowsAsync<IOException>(() => writer.WritePacketAsync(packet).AsTask());
            }
            Assert.Equal(205, second.Bytes.Length);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(second.WasDisposed);
            Assert.True(first.CanWrite);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task SegmentRequiresSupportedTrackSetAndPrecedingOutput(int scenario)
        {
            MediaBufferAllocator allocator = new(4096);
            using MemoryStream first = new();
            using MemoryStream second = new();
            EncodedStreamInfo[] streams = scenario switch
            {
                0 => [Audio(), Audio(2)],
                1 => [Video()],
                2 => [Video(), Video(2)],
                3 => [Audio()],
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            await using MpegTsWriter writer = MpegTsWriter.Create(first, streams, allocator);
            if (scenario is 0 or 2)
            {
                using MediaPacket packet = Packet(allocator, scenario == 0 ? 1 : 0, true, 0);
                await writer.WritePacketAsync(packet);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.BeginSegmentAsync(second).AsTask());
            Assert.Equal(0, second.Length);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static EncodedStreamInfo Video(int stream = 0) => new(stream, MediaKind.Video, "h264", StreamFormats.Avc, new(1, 1000),
            [1, 66, 0, 30, 0xFC, 0xE1, 0, 2, 0x67, 0x42, 1, 0, 2, 0x68, 0xCE]);

        private static EncodedStreamInfo Audio(int stream = 1) => new(stream, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 1000), [0x11, 0x88]);

        private static MediaPacket Packet(MediaBufferAllocator allocator, int stream, bool idr, int milliseconds)
        {
            using WritableMediaBuffer storage = allocator.Allocate(3);
            new byte[] { 2, (byte)(idr ? 0x65 : 0x41), 0x88 }.CopyTo(storage.Memory);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp timestamp = new(milliseconds, new TimeBase(1, 1000));
            return new(stream, buffer, timestamp, timestamp);
        }
    }
}
