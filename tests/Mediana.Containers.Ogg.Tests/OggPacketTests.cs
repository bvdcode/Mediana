// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Ogg;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Ogg.Tests
{
    public class OggPacketTests
    {
        [Theory]
        [InlineData("opus-stereo", 2, 69)]
        [InlineData("vorbis-stereo", 3, 61)]
        public async Task EveryRealEncodedPacketMatchesIndependentDemuxing(string name, int headers, int count)
        {
            byte[] data = await File.ReadAllBytesAsync(DataPath(name + ".ogg"));
            byte[] expected = await File.ReadAllBytesAsync(DataPath(name + ".packets"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(data, 13);
            await using (OggPacketReader reader = new(source, allocator))
            {
                int position = 0, packets = 0;
                while (await reader.ReadPacketAsync() is OggPacket packet)
                {
                    using (packet)
                    {
                        Assert.Equal(0, packet.LinkIndex);
                        Assert.Equal(packets, packet.PacketIndex);
                        Assert.Equal(packets == 0, packet.IsBeginning);
                        if (packets >= headers)
                        {
                            int size = BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(position));
                            position += 4;
                            Assert.Equal(expected.AsSpan(position, size).ToArray(), packet.Data.ToArray());
                            position += size;
                        }
                        if (packet.IsEnd) { Assert.NotNull(packet.GranulePosition); }
                        packets++;
                    }
                }
                Assert.Equal(headers + count, packets);
                Assert.Equal(expected.Length, position);
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.Disposed);
            Assert.Equal(data.Length, source.Position);
        }

        [Fact]
        public async Task MaximumPageContinuationEmptyPacketAndGranuleOwnershipArePreserved()
        {
            byte[] payload = Enumerable.Range(0, 65280).Select(value => (byte)value).ToArray();
            byte[] first = Page(2, -1, 37, 0, Enumerable.Repeat((byte)255, 255).ToArray(), payload[..65025]);
            byte[] second = Page(5, 42, 37, 1, [255, 0, 0, 3], [.. payload[65025..], 11, 12, 13]);
            MediaBufferAllocator allocator = new(512 * 1024);
            await using OggTestSource source = new([.. first, .. second], 7);
            await using (OggPacketReader reader = new(source, allocator))
            {
                using OggPacket complete = (await reader.ReadPacketAsync())!;
                Assert.Equal(payload, complete.Data.ToArray());
                Assert.True(complete.IsBeginning);
                Assert.Null(complete.GranulePosition);
                Assert.Equal(first.Length, complete.CompletionPageOffset);
                using OggPacket empty = (await reader.ReadPacketAsync())!;
                Assert.Empty(empty.Data.ToArray());
                Assert.Null(empty.GranulePosition);
                using OggPacket final = (await reader.ReadPacketAsync())!;
                Assert.Equal(new byte[] { 11, 12, 13 }, final.Data.ToArray());
                Assert.Equal(42, final.GranulePosition);
                Assert.True(final.IsEnd);
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InterleavedStreamsKeepSeparateSequencesAndChainedLinksCanReuseSerials()
        {
            byte[] data = [.. Page(2, 0, 31, 0, [1], [1]), .. Page(2, 0, 32, 0, [1], [2]),
                .. Page(4, 9, 31, 1, [1], [3]), .. Page(4, 10, 32, 1, [1], [4]),
                .. Page(2, 0, 31, 0, [1], [5]), .. Page(4, 8, 31, 1, [1], [6])];
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new(data);
            await using (OggPacketReader reader = new(source, allocator))
            {
                uint[] serials = [31, 32, 31, 32, 31, 31];
                long[] indices = [0, 0, 1, 1, 0, 1];
                for (int index = 0; index < 6; index++)
                {
                    using OggPacket packet = (await reader.ReadPacketAsync())!;
                    Assert.Equal(serials[index], packet.Serial);
                    Assert.Equal(indices[index], packet.PacketIndex);
                    Assert.Equal(index < 4 ? 0 : 1, packet.LinkIndex);
                    Assert.Equal(index + 1, packet.Data.Span[0]);
                }
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task StructuralDamageIsRejectedAndFaultsFurtherSequentialReads(int variant)
        {
            byte[] first = Page(2, 0, 23, 0, [1], [4]);
            byte[] last = variant switch
            {
                0 => Page(4, 1, 23, 2, [1], [8]),
                1 => Page(5, 1, 23, 1, [1], [8]),
                2 => Page(4, 1, 24, 1, [1], [8]),
                3 => Page(4, 1, 23, 1, [255], new byte[255]),
                4 => Page(12, 1, 23, 1, [1], [8]),
                5 => [],
                _ => throw new ArgumentOutOfRangeException(nameof(variant))
            };
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new([.. first, .. last]);
            await using (OggPacketReader reader = new(source, allocator))
            {
                using OggPacket header = (await reader.ReadPacketAsync())!;
                await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPacketAsync().AsTask());
                await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PageChecksumIsVerifiedBeforeAnyOfItsPacketsArePublished()
        {
            byte[] data = Page(6, 3, 29, 0, [1, 2], [4, 5, 6]);
            data[^1] ^= 1;
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new(data, 1);
            await using (OggPacketReader reader = new(source, allocator))
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadPacketAsync().AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RetainedPacketOutlivesReaderAndSinglePagePacketsShareStorage()
        {
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new(Page(6, 3, 9, 0, [1, 2], [4, 5, 6]));
            OggPacket first, second;
            await using (OggPacketReader reader = new(source, allocator))
            {
                first = (await reader.ReadPacketAsync())!;
                long live = allocator.AllocatedBytes;
                second = (await reader.ReadPacketAsync())!;
                Assert.Equal(live, allocator.AllocatedBytes);
                Assert.Null(await reader.ReadPacketAsync());
            }
            using MediaBuffer retained = second.RetainData();
            first.Dispose();
            second.Dispose();
            Assert.Equal(new byte[] { 5, 6 }, retained.Memory.ToArray());
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PacketBudgetIsEnforcedAcrossPageBoundariesAndDisposalReleasesPartialAssembly()
        {
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new([.. Page(2, -1, 11, 0, [255], new byte[255]),
                .. Page(5, 1, 11, 1, [1], [42])]);
            await using (OggPacketReader reader = new(source, allocator, new() { MaximumPacketBytes = 255 }))
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadPacketAsync().AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAndOverlappingOperationsLeaveSourceBorrowed()
        {
            MediaBufferAllocator allocator = new(4096);
            await using OggTestSource source = new(Page(6, 1, 11, 0, [1], [3]));
            OggPacketReader reader = new(source, allocator);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Gate = gate;
            using CancellationTokenSource cancellation = new();
            Task<OggPacket?> active = reader.ReadPacketAsync(cancellation.Token).AsTask();
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadPacketAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.DisposeAsync().AsTask());
            await cancellation.CancelAsync();
            gate.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.Disposed);
        }

        internal static byte[] Page(byte flags, long granule, uint serial, uint sequence, byte[] lacing, byte[] body)
        {
            Assert.Equal(body.Length, lacing.Sum(value => value));
            byte[] page = new byte[27 + lacing.Length + body.Length];
            "OggS"u8.CopyTo(page);
            page[5] = flags;
            BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), serial);
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
            page[26] = checked((byte)lacing.Length);
            lacing.CopyTo(page, 27);
            body.CopyTo(page, 27 + lacing.Length);
            uint crc = 0;
            foreach (byte value in page)
            {
                crc ^= (uint)value << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    bool top = (crc & 0x80000000) != 0;
                    crc <<= 1;
                    if (top) { crc ^= 0x04c11db7; }
                }
            }
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc);
            return page;
        }

        private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
    }
}
