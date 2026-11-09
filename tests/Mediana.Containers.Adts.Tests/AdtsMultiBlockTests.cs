// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.Adts;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AdtsMultiBlockTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task MixedOneToFourBlocksPreserveEverySampleAcrossSequentialAndRepeatedSeeks(bool protection, bool indexed) =>
            await AdtsMultiBlockTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), protection, indexed);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task IndexReadsOnlyHeadersAndAccountsForEveryRawBlock(bool protection)
        {
            byte[] input = await ReadAsync(protection);
            MediaBufferAllocator allocator = new(8192);
            await using AdtsReadTrackingSource source = new(input);
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            Assert.Equal(11, reader.GetPacketCount(0));
            Assert.Equal(77, source.Reads.Sum(read => read.Length));
            Assert.Equal(0, allocator.AllocatedBytes);
            int[] starts = [0, 1, 3, 6, 10, 12, 13, 14, 15, 16, 17];
            for (int index = 0; index < starts.Length; index++)
            {
                int count = index + 1 < starts.Length ? starts[index + 1] - starts[index] : 1;
                Assert.Equal(new MediaTimestamp(starts[index] * 1024, new(1, 48000)), reader.GetPacketTiming(0, index).PresentationTime);
                Assert.Equal(new MediaDuration(count * 1024, new(1, 48000)), reader.GetPresentationDuration(0, index));
            }
            Assert.Equal(3, reader.FindSeekPoint(0, new(8 * 1024, new(1, 48000)), 11)!.Value.PacketIndex);
            source.Reads.Clear();
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 3);
            Assert.Equal(packet.Data.Length, source.Reads.Sum(read => read.Length));
            Assert.Equal(0, source.Position);
        }

        [Fact]
        public async Task LastRawBlockCrcFailureIsAtomicAndResetDisposesEveryQueuedFrame()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using AdtsReadTrackingSource source = new(await ReadAsync(true));
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            using MediaPacket valid = await reader.ReadPacketAtAsync(0, 3);
            byte[] damaged = valid.Data.ToArray();
            damaged[^1] ^= 1;
            using WritableMediaBuffer writable = allocator.Allocate(damaged.Length);
            damaged.CopyTo(writable.Memory);
            using MediaBuffer storage = writable.Publish();
            using MediaPacket corrupted = new(0, storage, valid.PresentationTime, valid.DecodeTime, valid.Duration);
            long baseline = allocator.AllocatedBytes;
            AacDecoderFactory factory = new();
            Assert.Throws<InvalidDataException>(() => factory.CreateIntegrityValidator(reader.Streams[0]).Validate(corrupted));
            Assert.Throws<InvalidDataException>(() => factory.CreateRandomAccessInspector(reader.Streams[0]).IsRandomAccess(corrupted));
            using IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(reader.Streams[0], allocator);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(corrupted));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(valid));
            using (AudioFrame frame = decoder.Receive().Value) { Assert.Equal(1024, frame.SamplesPerChannel); }
            decoder.Reset();
            Assert.Equal(baseline, allocator.AllocatedBytes);
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
        }

        [Theory]
        [InlineData("FFF04C8033DFFF000200DD014E4C71")]
        [InlineData("FFF04C8033DFFF004E0001014E5EE1")]
        [InlineData("FFF04C8033DFFF004E00DDFFFFD7F8")]
        public async Task CrcCorrectPositionsCannotOverlapOrEscapeTheTransportFrame(string prefix)
        {
            MediaBufferAllocator allocator = new(8192);
            await using AdtsReadTrackingSource source = new(await ReadAsync(true));
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 3);
            byte[] data = packet.Data.ToArray();
            Convert.FromHexString(prefix).CopyTo(data, 0);
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => { _ = new AdtsFrameLayout(data); });
            Assert.Contains("positions", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PrerollCountsTransportPacketsAndCancellationReleasesTheEntireOutputQueue()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using AdtsReadTrackingSource source = new(await ReadAsync(true));
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 3);
            using MediaPacket second = await reader.ReadPacketAtAsync(0, 4);
            long baseline = allocator.AllocatedBytes;
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(reader.Streams[0], allocator);
            IPacketPrerollDecoder preroll = Assert.IsAssignableFrom<IPacketPrerollDecoder>(decoder);
            preroll.ResetForPreroll(1, true);
            decoder.TrySend(first);
            Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            decoder.TrySend(second);
            using (AudioFrame frame = decoder.Receive().Value)
            {
                Assert.Equal(second.PresentationTime, frame.Timestamp);
            }
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(new CancellationToken(true)));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(second);
            decoder.CompleteInput();
            for (int block = 0; block < 2; block++)
            {
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(new MediaTimestamp((10 + block) * 1024, new(1, 48000)), frame.Timestamp);
            }
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FailureToAllocateTheLastPcmFrameReleasesEveryEarlierFrameAndWorkspace()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using AdtsReadTrackingSource source = new(await ReadAsync(true));
            await using AdtsIndexedReader reader = await AdtsIndexedReader.OpenAsync(source, allocator);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 3);
            long baseline = allocator.AllocatedBytes;
            AacDecoderFactory factory = new();
            long fullBytes;
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = factory.Create(reader.Streams[0], allocator))
            {
                decoder.TrySend(packet);
                fullBytes = allocator.AllocatedBytes - baseline;
            }
            Assert.Equal(baseline, allocator.AllocatedBytes);
            MediaBufferAllocator limited = new(fullBytes - 1);
            using IMediaCodec<MediaPacket, AudioFrame> constrained = factory.Create(reader.Streams[0], limited);
            Assert.Throws<MediaMemoryLimitException>(() => constrained.TrySend(packet));
            Assert.Equal(0, limited.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => constrained.Receive());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RawProjectionRejectsMultipleBlocksBeforeReturningAnAmbiguousAccessUnit(bool indexed)
        {
            MediaBufferAllocator allocator = new(8192);
            byte[] input = await ReadAsync(false);
            AdtsReaderOptions options = new() { PacketFraming = AdtsPacketFraming.RawAccessUnit };
            await using AdtsReadTrackingSource source = new(input);
            if (indexed)
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => AdtsIndexedReader.OpenAsync(source, allocator, options).AsTask());
            }
            else
            {
                await using AdtsReader reader = await AdtsReader.OpenAsync(source, allocator, options);
                using (MediaPacket packet = (await reader.ReadPacketAsync())!) { Assert.NotNull(packet); }
                await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadPacketAsync().AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> ReadAsync(bool protection) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
            "Data", protection ? "multi-protected.aac" : "multi-unprotected.aac"));
    }
}
