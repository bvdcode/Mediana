// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Aiff;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AiffReaderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ShortReadsOffsetAndBlockFillPreserveOnlyDeclaredSamples(bool compressed)
        {
            byte[] data = [0x80, 0xff, 0, 0x40, 0x7f];
            List<(string, byte[])> chunks = [("JUNK", [1, 2, 3])];
            if (compressed)
            {
                chunks.Add(("FVER", [0xa2, 0x80, 0x51, 0x40]));
            }
            chunks.Add(("COMM", AiffTestInput.Common(8, 1, 5, compressed ? "NONE" : null)));
            chunks.Add(("SSND", AiffTestInput.Sound(data, offset: 3, blockSize: 16, trailingBytes: 8)));
            chunks.Add(("ANNO", [9]));
            byte[] file = AiffTestInput.File(compressed, chunks.ToArray());
            MediaBufferAllocator allocator = new(8192);
            await using ShortReadSource source = new(file);
            await using (AiffReader reader = await AiffReader.OpenAsync(source, allocator, new AiffReadOptions { SamplesPerPacket = 2 }))
            {
                Assert.Equal(5, reader.SamplesPerChannel);
                Assert.Equal(new MediaDuration(5, new TimeBase(1, 48000)), reader.Duration);
                for (int index = 0; index < 3; index++)
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    int samples = Math.Min(2, 5 - index * 2);
                    Assert.Equal(data.AsSpan(index * 2, samples).ToArray(), packet.Data.ToArray());
                    Assert.Equal(new MediaTimestamp(index * 2, new TimeBase(1, 48000)), packet.PresentationTime);
                    Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                    Assert.Equal(new MediaDuration(samples, new TimeBase(1, 48000)), packet.Duration);
                }
                Assert.Null(await reader.ReadPacketAsync());
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ZeroSamplesDoNotRequireASoundChunk(bool compressed)
        {
            List<(string, byte[])> chunks = [("COMM", AiffTestInput.Common(samples: 0, compression: compressed ? "NONE" : null))];
            if (compressed)
            {
                chunks.Add(("FVER", [0xa2, 0x80, 0x51, 0x40]));
            }
            MediaBufferAllocator allocator = new(8192);
            await using ShortReadSource source = new(AiffTestInput.File(compressed, chunks.ToArray()));
            await using (AiffReader reader = await AiffReader.OpenAsync(source, allocator))
            {
                Assert.Single(reader.Streams);
                Assert.Null(await reader.ReadPacketAsync());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MalformedChunksAndSampleExtentsReleaseOpenStorage()
        {
            byte[] common = AiffTestInput.Common();
            byte[] sound = AiffTestInput.Sound(new byte[8]);
            List<byte[]> files = [[], AiffTestInput.File(false),
                AiffTestInput.File(false, ("COMM", common[..^1])),
                AiffTestInput.File(false, ("COMM", common), ("COMM", common)),
                AiffTestInput.File(false, ("COMM", common), ("SSND", sound[..^1])),
                AiffTestInput.File(false, ("COMM", common), ("SSND", new byte[7])),
                AiffTestInput.File(true, ("FVER", new byte[3]))];
            byte[] offset = (byte[])sound.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(offset, uint.MaxValue);
            files.Add(AiffTestInput.File(false, ("COMM", common), ("SSND", offset)));
            byte[] overflow = AiffTestInput.File(false, ("COMM", common), ("SSND", sound));
            BinaryPrimitives.WriteUInt32BigEndian(overflow.AsSpan(16), uint.MaxValue);
            files.Add(overflow);
            foreach (byte[] bytes in files)
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await AiffReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.False(source.IsDisposed);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TruncatedAudioAndCancellationFaultTheReader(bool cancel)
        {
            byte[] bytes = AiffTestInput.File(false, ("COMM", AiffTestInput.Common()), ("SSND", AiffTestInput.Sound(new byte[8])));
            MediaBufferAllocator allocator = new(8192);
            await using ShortReadSource source = new(bytes[..^1]);
            await using AiffReader reader = await AiffReader.OpenAsync(source, allocator);
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
        public async Task DuplicateTailChunksAndInvalidPaddingAreRejectedOnDrain()
        {
            byte[] common = AiffTestInput.Common(8, 1, 1);
            byte[] sound = AiffTestInput.Sound([0]);
            byte[] duplicate = AiffTestInput.File(false, ("COMM", common), ("SSND", sound), ("SSND", sound));
            byte[] padding = AiffTestInput.File(false, ("COMM", common), ("SSND", sound));
            padding[^1] = 1;
            foreach (byte[] bytes in new[] { duplicate, padding, padding[..^1] })
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await using (AiffReader reader = await AiffReader.OpenAsync(source, allocator))
                {
                    using MediaPacket packet = (await reader.ReadPacketAsync())!;
                    await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadPacketAsync());
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task LimitsAndUnsupportedOrderingAreExplicit()
        {
            byte[] common = AiffTestInput.Common();
            byte[] sound = AiffTestInput.Sound(new byte[8]);
            foreach (AiffReadOptions options in new[] { new AiffReadOptions { MaxChunks = 1 }, new AiffReadOptions { MaxMetadataBytes = 20 } })
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(AiffTestInput.File(false, ("COMM", common), ("SSND", sound)));
                await Assert.ThrowsAsync<NotSupportedException>(async () => await AiffReader.OpenAsync(source, allocator, options));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            foreach (byte[] bytes in new[]
            {
                AiffTestInput.File(false, ("SSND", sound), ("COMM", common)),
                AiffTestInput.File(true, ("COMM", AiffTestInput.Common(compression: "NONE")), ("SSND", sound)),
                AiffTestInput.File(true, ("FVER", [0, 0, 0, 0]))
            })
            {
                MediaBufferAllocator allocator = new(8192);
                await using ShortReadSource source = new(bytes);
                await Assert.ThrowsAsync<NotSupportedException>(async () => await AiffReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task KnownTruncatedFormAndAllocationFailuresPreserveTheSource()
        {
            byte[] bytes = AiffTestInput.File(false, ("COMM", AiffTestInput.Common()), ("SSND", AiffTestInput.Sound(new byte[8])));
            MediaBufferAllocator small = new(4096);
            await using ShortReadSource limited = new(bytes);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await AiffReader.OpenAsync(limited, small));
            Assert.Equal(0, small.AllocatedBytes);
            MediaBufferAllocator allocator = new(8192);
            using MemoryStream stream = new(bytes[..^1]);
            await using StreamByteSource source = new(stream, leaveOpen: true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await AiffReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(stream.CanRead);
        }
    }
}
