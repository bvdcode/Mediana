// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Vp8;
using Mediana.Codecs.Vp9;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Formats.Video;
using Mediana.Streaming.Hls;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    public class MatroskaVideoPresentationTests
    {
        [Theory]
        [InlineData("Vp8", "motion", 24)]
        [InlineData("Vp8", "altref", 72)]
        [InlineData("Vp9", "main8", 16)]
        [InlineData("Vp9", "altref", 48)]
        public async Task ColdReverseHlsRequestsUseVisiblePicturesAndContinuousExactClocks(string codec, string name, int pictures)
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", codec, name + ".webm"));
            IVideoDecoderFactory decoder = codec switch
            {
                "Vp8" => new Vp8DecoderFactory(),
                "Vp9" => new Vp9DecoderFactory(),
                _ => throw new ArgumentOutOfRangeException(nameof(codec))
            };
            await VpxHlsTestInput.VerifyAsync(data, decoder, pictures);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HiddenVp8PacketsMayShareTheNextVisibleTimestampWithOrWithoutTheContainerInvisibleFlag(bool invisible)
        {
            byte[][] payloads = await Vp8PayloadsAsync();
            byte[] data = MatroskaVideoTestData.Document("V_VP8", 41_666_667,
                Block(payload: payloads[0]), Block(timestamp: 42, flags: invisible ? (byte)8 : (byte)0, payload: payloads[1]),
                Block(timestamp: 42, flags: 0, payload: payloads[2]));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (TrackingRandomAccessSource source = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.Equal(3, reader.GetPacketCount(0));
                Assert.Equal(2, reader.GetPresentationUnitCount(0));
                Assert.Equal(2, reader.GetPresentationUnit(0, 1).PacketIndex);
                Assert.Null(reader.GetPresentationDuration(0, 1));
                IRandomAccessInspector inspector = ((IRandomAccessDecoderFactory)new Vp8DecoderFactory()).CreateRandomAccessInspector(reader.Streams[0]);
                IndexedPacketRange copy = await IndexedCopyPlanner.CreateAsync(reader, 0,
                    new(new(0, new(1, 1_000_000_000)), new(83_666_667, new(1, 1_000_000_000))), inspector);
                Assert.Equal(new IndexedPacketRange(0, 3), copy);
                using MediaPacket hidden = await reader.ReadPacketAtAsync(0, 1);
                Assert.Equal(payloads[1], hidden.Data.ToArray());
                Assert.Equal(reader.GetPacketTiming(0, 1).PresentationTime, reader.GetPacketTiming(0, 2).PresentationTime);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            await VpxHlsTestInput.VerifyAsync(data, new Vp8DecoderFactory(), 2);
        }

        [Fact]
        public async Task ExplicitShortBlockDurationsPreserveGaps()
        {
            byte[][] payloads = await Vp8PayloadsAsync();
            byte[] data = MatroskaVideoTestData.Document("V_VP8", null,
                Field(0xA0, [.. Block(0xA1, flags: 0, payload: payloads[0]), .. Unsigned(0x9B, 10)]),
                Field(0xA0, [.. Block(0xA1, timestamp: 40, flags: 0, payload: payloads[2]), .. Unsigned(0x9B, 10)]));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (TrackingRandomAccessSource source = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                MediaDuration extent = reader.GetPresentationDuration(0, 0)!.Value;
                Assert.Equal(new MediaTimestamp(10, new(1, 1000)), new(extent.Value, extent.TimeBase));
                Assert.Equal(extent, reader.GetPresentationDuration(0, 1));
                VideoFormat format = await VpxHlsTestInput.ReadFormatAsync(data, new Vp8DecoderFactory(), allocator);
                await using AvcHlsTranscodeSession hls = AvcHlsTranscodeSession.Create(reader, 0, new Vp8DecoderFactory(), format,
                    [new HlsVideoWindow(0, new(0, new(1, 1000)), new(50, new(1, 1000)))], allocator);
                using MemoryStream output = new();
                await Assert.ThrowsAsync<InvalidDataException>(() => hls.WriteSegmentAsync(0, output).AsTask());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task UnknownTailRemainsUnknownInsteadOfInventingAFramePeriod()
        {
            byte[][] payloads = await Vp8PayloadsAsync();
            byte[] data = MatroskaVideoTestData.Document("V_VP8", null, Block(payload: payloads[0]),
                Block(timestamp: 40, flags: 0, payload: payloads[2]));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (TrackingRandomAccessSource source = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.NotNull(reader.GetPresentationDuration(0, 0));
                Assert.Null(reader.GetPresentationDuration(0, 1));
                Assert.Throws<NotSupportedException>(() => VideoSegmentPlanner.Create(reader, 0, new(1, new(1, 1)), allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ContainerDisplayContradictionsAndTruncatedTagsFailWithoutRetainingIndexStorage()
        {
            byte[][] payloads = await Vp8PayloadsAsync();
            foreach (byte[] data in new[]
            {
                MatroskaVideoTestData.Document("V_VP8", 40_000_000, Block(flags: 0x88, payload: payloads[0])),
                MatroskaVideoTestData.Document("V_VP8", 40_000_000, Block(payload: [0, 1])),
                MatroskaVideoTestData.Document("V_VP9", 40_000_000, Block(payload: [0])),
                MatroskaVideoTestData.Document("V_VP9", 40_000_000, Block(payload: [0x82, 0xc0, 0, 0xc0]))
            })
            {
                MediaBufferAllocator allocator = new(4 * 1024 * 1024);
                await using TrackingRandomAccessSource source = new(data);
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                {
                    await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
                });
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task AllHiddenStreamsKeepTheirCodedPacketsButCannotProduceAHlsPlan()
        {
            byte[][] payloads = await Vp8PayloadsAsync();
            byte[] data = MatroskaVideoTestData.Document("V_VP8", 40_000_000, Block(flags: 8, payload: payloads[1]));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (TrackingRandomAccessSource source = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.Equal(1, reader.GetPacketCount(0));
                Assert.Equal(0, reader.GetPresentationUnitCount(0));
                long retained = allocator.AllocatedBytes;
                source.Reads.Clear();
                Assert.Throws<InvalidDataException>(() => VideoSegmentPlanner.Create(reader, 0, new(1, new(1, 1)), allocator));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPresentationUnit(0, 0));
                Assert.Empty(source.Reads);
                Assert.Equal(retained, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAndIndexBudgetFailuresReleasePresentationAndPrefixStorage()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Vp8", "altref.webm"));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            foreach (int maximum in new[] { 1, 64, 1024, 4096 })
            {
                await using TrackingRandomAccessSource source = new(data);
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                {
                    await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator, new() { MaxIndexBytes = maximum });
                });
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            long prefixOffset;
            await using (TrackingRandomAccessSource indexed = new(data))
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(indexed, allocator))
            {
                prefixOffset = indexed.Reads.Last(read => read.Length == Vp8FrameTag.HeaderBytes).Offset;
            }
            using CancellationTokenSource cancellation = new();
            await using TrackingRandomAccessSource cancelled = new(data);
            cancelled.AfterRead = () =>
            {
                if (cancelled.Reads[^1] == (prefixOffset, Vp8FrameTag.HeaderBytes)) { cancellation.Cancel(); }
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await using MatroskaReader reader = await MatroskaReader.OpenAsync(cancelled, allocator, cancellationToken: cancellation.Token);
            });
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public Task NativeCompatibleColdTailSmokeUsesTheSameVisiblePictureContracts() =>
            VpxHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        private static async Task<byte[][]> Vp8PayloadsAsync()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Vp8", "altref.webm")));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator);
            byte[][] payloads = new byte[3][];
            for (int packet = 0; packet < payloads.Length; packet++)
            {
                using MediaPacket encoded = await reader.ReadPacketAtAsync(0, packet);
                payloads[packet] = encoded.Data.ToArray();
            }
            return payloads;
        }
    }
}
