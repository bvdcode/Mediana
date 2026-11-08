// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AdtsIndexedReaderTests
    {
        private static byte[] Input => Convert.FromHexString("FFF15080013FFC1122FFF05080017FFC12343344FFF15080013FFC5566FFF05080017FFC56787788");

        [Fact]
        public async Task OpeningReadsEveryHeaderAndNoCompressedPayloadOrCrc()
        {
            await using AdtsReadTrackingSource source = new(Input);
            MediaBufferAllocator allocator = new(4096);
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            Assert.Equal(4, reader.GetPacketCount(0));
            Assert.Equal(28, source.Reads.Sum(item => item.Length));
            long[] expected = [.. Enumerable.Range(0, 7).Select(value => (long)value),
                .. Enumerable.Range(9, 7).Select(value => (long)value),
                .. Enumerable.Range(20, 7).Select(value => (long)value),
                .. Enumerable.Range(29, 7).Select(value => (long)value)];
            Assert.Equal(expected, source.Reads.Select(item => item.Offset));
            Assert.Equal(0, source.Position);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(new MediaDuration(4096, new TimeBase(1, 44100)), reader.Streams[0].CodedDuration);
            Assert.Null(reader.Info.DeclaredDuration);
        }

        [Fact]
        public async Task RepeatedOutOfOrderReadsPreserveBothCursorsAndRetainedStorage()
        {
            await using AdtsReadTrackingSource source = new(Input);
            await source.ReadAsync(new byte[1]);
            MediaBufferAllocator allocator = new(4096);
            MediaPacket retained;
            await using (AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator))
            {
                foreach (int index in new[] { 3, 1, 3, 0, 2 })
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                    Assert.Equal(new byte[] { (byte)(0x11 + index * 0x22), (byte)(0x22 + index * 0x22) }, packet.Data.ToArray());
                    Assert.Equal(new MediaTimestamp(index * 1024, new TimeBase(1, 44100)), packet.PresentationTime);
                }
                retained = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.Equal(0, retained.PresentationTime!.Value.Value);
                for (int index = 1; index < 4; index++)
                {
                    using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                    Assert.Equal(index * 1024, packet.PresentationTime!.Value.Value);
                }
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(1, source.Position);
            Assert.False(source.IsDisposed);
            Assert.Equal(new byte[] { 0x11, 0x22 }, retained.Data.ToArray());
            Assert.Equal(2, allocator.AllocatedBytes);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ClockAndPrerollQueriesReadNoPayloadAndRespectExclusiveBounds()
        {
            await using AdtsReadTrackingSource source = new(Input);
            MediaBufferAllocator allocator = new(4096);
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            TimeBase clock = new(1, 44100);
            MediaTimestamp target = new(3172, clock);
            Assert.Equal(3, reader.FindSeekPoint(0, target, 4)!.Value.PacketIndex);
            Assert.Equal(1, reader.FindSeekPoint(0, target, 2)!.Value.PacketIndex);
            Assert.Equal(0, reader.FindSeekPoint(0, new(-1, clock), 4)!.Value.PacketIndex);
            Assert.Null(reader.FindSeekPoint(0, target, 0));
            Assert.Equal(new ContainerSeekPoint(0, 1, new(1024, clock)), reader.GetPrerollStart(0, 3, 2));
            Assert.Equal(0, reader.GetPrerollStart(0, 0, 100).PacketIndex);
            Assert.Equal(new MediaDuration(1024, clock), reader.GetPresentationDuration(0, 3));
            ContainerPacketTiming timing = reader.GetPacketTiming(0, 3);
            Assert.Equal(new MediaTimestamp(3072, clock), timing.PresentationTime);
            Assert.Equal(timing.PresentationTime, timing.DecodeTime);
            Assert.Null(timing.IsKeyFrame);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPacketCount(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPacketTiming(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.FindSeekPoint(0, target, 5));
            Assert.Equal(28, source.Reads.Count);
        }

        [Fact]
        public async Task CancelledIndexedReadCanBeRetriedWithoutChangingTheSequentialCursor()
        {
            await using AdtsReadTrackingSource source = new(Input);
            MediaBufferAllocator allocator = new(4096);
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadPacketAtAsync(0, 3, cancellation.Token).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
            Assert.Equal(0, packet.PresentationTime!.Value.Value);
            Assert.Equal(new byte[] { 0x11, 0x22 }, packet.Data.ToArray());
        }

        [Theory]
        [InlineData("FFF15080013FFC11")]
        [InlineData("FFF15080013FFC1122FF")]
        [InlineData("FFF15080013FFC1122FFF14C80013FFC3344")]
        public async Task IncompleteOrChangingFramesFailDuringIndexing(string hex)
        {
            await using AdtsReadTrackingSource source = new(Convert.FromHexString(hex));
            MediaBufferAllocator allocator = new(4096);
            if (hex.Contains("FFF14C", StringComparison.Ordinal))
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => AdtsIndexedReader.OpenAsync(source, allocator).AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => AdtsIndexedReader.OpenAsync(source, allocator).AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}
