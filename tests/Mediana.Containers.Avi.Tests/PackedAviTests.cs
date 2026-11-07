// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Video;

namespace Mediana.Containers.Avi.Tests
{
    public class PackedAviTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        public async Task PackedIndexSeparatesCodedPicturesFromPhysicalTransportClocks(int start)
        {
            byte[] bytes = await InputAsync();
            int streamHeader = bytes.AsSpan().IndexOf("strh"u8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(streamHeader + 8 + 28), (uint)start);
            await using AviTestSource source = new(bytes, 3);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                long[] presentation = [0, 3, 1, 2, 6, 4, 5, 9, 7, 8, 12, 10, 11, 15, 13, 14, 18, 16, 17, 21, 19, 20];
                long[] decode = [0, 3, 3, 4, 6, 6, 7, 9, 9, 10, 12, 12, 13, 15, 15, 16, 18, 18, 19, 21, 21, 22];
                Assert.Equal(presentation.Length, reader.GetPacketCount(0));
                Assert.Equal(24, reader.Info.StreamInfo![0].PacketIndex!.DecodeDuration!.Value.Value);
                long reads = source.ReadBytes;
                for (int index = 0; index < presentation.Length; index++)
                {
                    ContainerPacketTiming timing = reader.GetPacketTiming(0, index);
                    Assert.Equal(new MediaTimestamp(start + presentation[index], new(1, 12)), timing.PresentationTime);
                    Assert.Equal(new MediaTimestamp(start + decode[index], new(1, 12)), timing.DecodeTime);
                    Assert.Equal(index == 0 ? 3 : 1, timing.Duration!.Value.Value);
                    Assert.Equal(new MediaDuration(1, new(1, 12)), reader.GetPresentationDuration(0, index));
                    ContainerSeekPoint seek = reader.FindSeekPoint(0, new(start + presentation[index], new(1, 12)), presentation.Length)!.Value;
                    Assert.True(reader.GetPacketTiming(0, seek.PacketIndex).IsKeyFrame);
                    Assert.True(seek.PresentationTime.CompareTo(timing.PresentationTime!.Value) <= 0);
                }
                Assert.Equal(reads, source.ReadBytes);
                Assert.True(allocator.AllocatedBytes < bytes.Length);
                for (int index = 0; index < presentation.Length; index++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                    int pictures = 0;
                    for (int at = Mpeg4VisualStartCodes.Find(packet.Data.Span); at >= 0; at = Mpeg4VisualStartCodes.Find(packet.Data.Span, at + 4))
                    { if (packet.Data.Span[at + 3] == 0xb6) { pictures++; } }
                    Assert.Equal(1, pictures);
                }
                using MediaPacket retained = await reader.ReadPacketAtAsync(0, 2);
                await reader.DisposeAsync();
                Assert.Equal(retained.Data.Length, allocator.AllocatedBytes);
                Assert.False(source.Disposed);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task BothPartsOfASplitPacketValidateTheirOriginalPhysicalChunk(int packetIndex)
        {
            byte[] bytes = await InputAsync();
            await using AviTestSource source = new(bytes);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                int movie = bytes.AsSpan().IndexOf("movi"u8) + 4;
                int offset = movie + bytes.AsSpan(movie).IndexOf("00dc"u8);
                int firstLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4)));
                int secondChunk = offset + 8 + firstLength + (firstLength & 1);
                while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(secondChunk + 4)) == 0) { secondChunk += 8; }
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(secondChunk + 4));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secondChunk + 4), length - 1);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadPacketAtAsync(0, packetIndex));
                Assert.Throws<InvalidOperationException>(() => reader.GetPacketCount(0));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ARealUncodedPresentationIsNotDiscardedAsAPackedPlaceholder()
        {
            byte[] bytes = await InputAsync();
            int dummy = FindDummy(bytes);
            bytes[dummy + 4] = (byte)((bytes[dummy + 4] & 0xf0) | 4);
            await using AviTestSource source = new(bytes);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await AviReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidPlaceholderStuffingCannotHideUnvalidatedPayload()
        {
            byte[] bytes = await InputAsync();
            bytes[FindDummy(bytes) + 5] ^= 1;
            await using AviTestSource source = new(bytes);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await AviReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(25000)]
        [InlineData(30000)]
        [InlineData(50000)]
        public async Task PackedIndexAllocationFailureReleasesPhysicalAndNormalizedMetadata(int limit)
        {
            await using AviTestSource source = new(await InputAsync());
            MediaBufferAllocator allocator = new(limit);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await AviReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.Disposed);
        }

        [Fact]
        public async Task CancellingDuringPackedIndexingReleasesAllStorage()
        {
            using CancellationTokenSource cancellation = new();
            await using AviTestSource source = new(await InputAsync(), 3,
                read => { if (read > 20000) { cancellation.Cancel(); } });
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await AviReader.OpenAsync(source, allocator, cancellation.Token));
            Assert.True(source.ReadBytes > 20000);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static int FindDummy(byte[] bytes)
        {
            int at = bytes.AsSpan().IndexOf(new byte[] { 0, 0, 1, 0xb6, 0x53, 0x9f });
            Assert.True(at >= 0);
            return at;
        }

        private static Task<byte[]> InputAsync() => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mpeg4Visual", "xvid.avi"));
    }
}
