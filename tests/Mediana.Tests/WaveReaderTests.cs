// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Wave;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class WaveReaderTests
    {
        [Fact]
        public async Task ShortReadsAndOddChunksPreserveExactPacketsAndBorrowTheSource()
        {
            byte[] data = [0, 64, 128, 192, 255];
            byte[] file = WaveTestInput.File(("JUNK", [9, 8, 7]), ("fmt ", WaveTestInput.Format(8, 1, 8000)),
                ("data", data), ("LIST", [.. "INFO"u8, 1, 2, 3]));
            MediaBufferAllocator allocator = new(8192);
            await using ShortReadSource source = new(file);
            await using (WaveReader reader = await WaveReader.OpenAsync(source, allocator, new WaveReadOptions { SamplesPerPacket = 2 }))
            {
                Assert.Equal(5, reader.SamplesPerChannel);
                Assert.Equal(new MediaDuration(5, new TimeBase(1, 8000)), reader.Duration);
                Assert.Equal(reader.Duration, Assert.Single(reader.Streams).CodedDuration);
                for (int index = 0; index < 3; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    Assert.Equal(data.AsSpan(index * 2, Math.Min(2, 5 - index * 2)).ToArray(), packet.Data.ToArray());
                    Assert.Equal(new MediaTimestamp(index * 2, new TimeBase(1, 8000)), packet.PresentationTime);
                    Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                    Assert.Equal(new MediaDuration(packet.Data.Length, new TimeBase(1, 8000)), packet.Duration);
                }
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidHeadersAndChunkExtentsReleaseAllOpenStorage()
        {
            byte[] valid = WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", [1, 2, 3, 4]));
            List<byte[]> damaged = [[], valid[..11], WaveTestInput.File(("data", [1, 2, 3, 4])),
                WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("fmt ", WaveTestInput.Format()), ("data", [])),
                WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", [1, 2, 3]))];
            byte[] overflow = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(overflow.AsSpan(16), uint.MaxValue);
            damaged.Add(overflow);
            byte[] outer = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(outer.AsSpan(4), uint.MaxValue);
            foreach (byte[] bytes in damaged)
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await WaveReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.False(source.IsDisposed);
            }
            MediaBufferAllocator knownAllocator = new(8192);
            using MemoryStream stream = new(outer);
            await using StreamByteSource known = new(stream, leaveOpen: true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await WaveReader.OpenAsync(known, knownAllocator));
            Assert.Equal(0, knownAllocator.AllocatedBytes);
            Assert.True(stream.CanRead);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TruncationAndCancellationFaultTheReaderAndReleaseItsWorkingStorage(bool cancel)
        {
            byte[] bytes = WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", new byte[32]));
            await using ShortReadSource source = new(bytes[..^1]);
            MediaBufferAllocator allocator = new(8192);
            await using WaveReader reader = await WaveReader.OpenAsync(source, allocator);
            if (cancel)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadPacketAsync(new CancellationToken(true)));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadPacketAsync());
        }

        [Fact]
        public async Task LimitsAndAllocationFailureAreExplicitAndDoNotCloseTheSource()
        {
            byte[] bytes = WaveTestInput.File(("JUNK", new byte[32]), ("fmt ", WaveTestInput.Format()), ("data", [1, 2, 3, 4]));
            foreach (WaveReadOptions options in new[] { new WaveReadOptions { MaxChunks = 1 }, new WaveReadOptions { MaxMetadataBytes = 20 } })
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await Assert.ThrowsAsync<NotSupportedException>(async () => await WaveReader.OpenAsync(source, allocator, options));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.False(source.IsDisposed);
            }
            MediaBufferAllocator small = new(4096);
            await using ShortReadSource limited = new(bytes);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await WaveReader.OpenAsync(limited, small));
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Fact]
        public async Task DuplicateDataAndMissingOddPaddingAreRejectedOnDrain()
        {
            byte[] duplicate = WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", [1, 2, 3, 4]), ("data", []));
            byte[] padded = WaveTestInput.File(("fmt ", WaveTestInput.Format(8, 1)), ("data", [1]));
            foreach (byte[] bytes in new[] { duplicate, padded[..^1] })
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await using WaveReader reader = await WaveReader.OpenAsync(source, allocator);
                using (MediaPacket? packet = await reader.ReadPacketAsync())
                {
                    Assert.NotNull(packet);
                }
                await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadPacketAsync());
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NestedAudioListsCannotSilentlyOmitAdditionalSamples(bool trailing)
        {
            byte[] format = WaveTestInput.Format();
            byte[] list = [.. "wavl"u8];
            byte[] bytes = trailing
                ? WaveTestInput.File(("fmt ", format), ("data", [0, 0, 0, 0]), ("LIST", list))
                : WaveTestInput.File(("fmt ", format), ("LIST", list), ("data", [0, 0, 0, 0]));
            MediaBufferAllocator allocator = new(8192);
            await using ShortReadSource source = new(bytes);
            if (trailing)
            {
                await using WaveReader reader = await WaveReader.OpenAsync(source, allocator);
                using (MediaPacket? packet = await reader.ReadPacketAsync())
                {
                    Assert.NotNull(packet);
                }
                await Assert.ThrowsAsync<NotSupportedException>(async () => await reader.ReadPacketAsync());
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () => await WaveReader.OpenAsync(source, allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
