// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AdtsReaderTests
    {
        [Fact]
        public async Task SingleByteReadsPreservePayloadTimingAndPacketOwnership()
        {
            byte[] frame = Convert.FromHexString("FFF15080013FFC1122");
            byte[] protectedFrame = Convert.FromHexString("FFF05080017FFC12343344");
            await using ShortReadSource source = new([.. frame, .. protectedFrame]);
            MediaBufferAllocator allocator = new(4096);
            MediaPacket retained;
            await using (AdtsReader reader = await AdtsReader.OpenAsync(source, allocator, new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit }))
            {
                Assert.Equal(9, allocator.AllocatedBytes);
                Assert.Null(reader.CodedDuration);
                Assert.Equal("adts", reader.Info.FormatId);
                EncodedStreamInfo stream = Assert.Single(reader.Streams);
                Assert.Equal(StreamFormats.Aac, stream.Format);
                Assert.Equal(Convert.FromHexString("1210"), stream.Configuration.ToArray());
                using MediaPacket first = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.Equal(new byte[] { 0x11, 0x22 }, first.Data.ToArray());
                Assert.Equal(new MediaTimestamp(0, new TimeBase(1, 44100)), first.PresentationTime);
                retained = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.Equal(new MediaTimestamp(1024, new TimeBase(1, 44100)), retained.PresentationTime);
                Assert.Equal(retained.PresentationTime, retained.DecodeTime);
                Assert.Equal(new MediaDuration(1024, new TimeBase(1, 44100)), retained.Duration);
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Equal(new MediaDuration(2048, new TimeBase(1, 44100)), reader.CodedDuration);
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(new byte[] { 0x33, 0x44 }, retained.Data.ToArray());
            Assert.True(retained.TryGetSideData(AdtsPacketProtection.SideDataKind, out MediaPacketSideDataView protection));
            Assert.Equal(protectedFrame[..9], protection.Data.ToArray());
            Assert.Equal(protectedFrame.Length, allocator.AllocatedBytes);
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("FF")]
        [InlineData("FFF15080013FFC11")]
        [InlineData("00000000000000")]
        public async Task TruncationAndLostSyncReleaseStorageWithoutInvalidatingEarlierPackets(string tail)
        {
            await using ShortReadSource source = new(Convert.FromHexString("FFF15080013FFC1122" + tail));
            MediaBufferAllocator allocator = new(4096);
            await using AdtsReader reader = await AdtsReader.OpenAsync(source, allocator, new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit });
            using MediaPacket retained = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Equal(2, allocator.AllocatedBytes);
            Assert.Equal(new byte[] { 0x11, 0x22 }, retained.Data.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
        }

        [Theory]
        [InlineData("FFF14C80013FFC1122")]
        [InlineData("FFF15040013FFC1122")]
        [InlineData("FFF11080013FFC1122")]
        [InlineData("FFF95080013FFC1122")]
        public async Task ConfigurationChangesFailBeforeReturningAnotherPacket(string tail)
        {
            await using ShortReadSource source = new(Convert.FromHexString("FFF15080013FFC1122" + tail));
            MediaBufferAllocator allocator = new(4096);
            await using AdtsReader reader = await AdtsReader.OpenAsync(source, allocator, new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit });
            using (MediaPacket? first = await reader.ReadPacketAsync()) { Assert.NotNull(first); }
            await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MultipleBlocksFailExplicitlyInsteadOfJoiningAccessUnits()
        {
            await using ShortReadSource source = new(Convert.FromHexString("FFF15080017FFD11223344"));
            MediaBufferAllocator allocator = new(4096);
            await Assert.ThrowsAsync<NotSupportedException>(() => AdtsReader.OpenAsync(source, allocator, new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit }).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OpeningReadsOnlyTheHeaderAndLeavesPayloadForTheFirstPacket()
        {
            await using GatedByteSource source = new(Convert.FromHexString("FFF15080013FFC1122"));
            MediaBufferAllocator allocator = new(4096);
            await using AdtsReader reader = await AdtsReader.OpenAsync(source, allocator, new AdtsReaderOptions { PacketFraming = AdtsPacketFraming.RawAccessUnit });
            source.GateReads = true;
            using CancellationTokenSource cancellation = new();
            Task<MediaPacket?> pending = reader.ReadPacketAsync(cancellation.Token).AsTask();
            await source.Entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
            Assert.Throws<InvalidOperationException>(() => reader.DisposeAsync());
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
