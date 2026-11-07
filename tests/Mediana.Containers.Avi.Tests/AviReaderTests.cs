// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Pcm;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing;

namespace Mediana.Containers.Avi.Tests
{
    public class AviReaderTests
    {
        [Theory]
        [InlineData(AviIndexKind.Relative)]
        [InlineData(AviIndexKind.Absolute)]
        [InlineData(AviIndexKind.Standard)]
        [InlineData(AviIndexKind.Super)]
        [InlineData(AviIndexKind.None)]
        public async Task EveryNativeIndexPreservesOffsetsFractionalClockAndIndependentCursors(AviIndexKind kind)
        {
            foreach (bool records in new[] { false, true })
            {
                await using AviTestSource source = new(AviTestData.Build(kind, records), 3);
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using AviReader reader = await AviReader.OpenAsync(source, allocator);
                Assert.Equal("avi", reader.Info.FormatId);
                Assert.Equal(3, reader.GetPacketCount(0));
                Assert.Equal(new TimeBase(1001, 30000), reader.Streams[0].TimeBase);
                long reads = source.ReadBytes;
                ContainerPacketTiming timing = reader.GetPacketTiming(0, 2);
                Assert.Equal(new MediaTimestamp(5, new(1001, 30000)), timing.PresentationTime);
                Assert.Equal(1, timing.Duration!.Value.Value);
                Assert.Equal(1, reader.FindSeekPoint(0, new(4, new(1001, 30000)), 3)!.Value.PacketIndex);
                Assert.Equal(0, reader.FindSeekPoint(0, new(0, new(1, 1)), 3)!.Value.PacketIndex);
                Assert.Equal(reads, source.ReadBytes);
                using MediaPacket last = await reader.ReadPacketAtAsync(0, 2);
                using MediaPacket first = (await reader.ReadPacketAsync())!;
                Assert.Equal(3, first.PresentationTime!.Value.Value);
                Assert.Equal(5, last.PresentationTime!.Value.Value);
                Assert.Equal(first.Data.ToArray(), last.Data.ToArray());
                await reader.DisposeAsync();
                Assert.False(source.Disposed);
                Assert.Equal(14, allocator.AllocatedBytes);
                Assert.Equal(0xff, first.Data.Span[0]);
                first.Dispose(); last.Dispose();
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(AviIndexKind.Relative)]
        [InlineData(AviIndexKind.Super)]
        [InlineData(AviIndexKind.None)]
        public async Task EmptyVideoChunksExtendThePreviousDisplayedPictureWithoutInventingPayload(AviIndexKind kind)
        {
            await using AviTestSource source = new(AviTestData.Build(kind, dropped: true));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using AviReader reader = await AviReader.OpenAsync(source, allocator);
            Assert.Equal(3, reader.GetPacketCount(0));
            Assert.Equal(2, reader.GetPacketTiming(0, 0).Duration!.Value.Value);
            Assert.Equal(5, reader.GetPacketTiming(0, 1).PresentationTime!.Value.Value);
            Assert.Equal(4, reader.Info.StreamInfo![0].PacketIndex!.DecodeDuration!.Value.Value);
        }

        [Fact]
        public async Task IndependentInterleavedReferenceMatchesEveryPacketWithoutReadingMediaAtOpen()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "mjpeg-pcm.avi"));
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "mjpeg-pcm.json")));
            await using AviTestSource source = new(bytes, 67);
            MediaBufferAllocator allocator = new(1024 * 1024, MemoryPool<byte>.Shared);
            await using AviReader reader = await AviReader.OpenAsync(source, allocator);
            Assert.True(source.ReadBytes < bytes.Length / 10);
            Assert.Equal(2, reader.Streams.Count);
            Assert.Equal(new TimeBase(1, 8), reader.Streams[0].TimeBase);
            Assert.Equal(new TimeBase(1, 48000), reader.Streams[1].TimeBase);
            Assert.Equal("mjpeg", reader.Streams[0].CodecId);
            Assert.Equal("pcm", reader.Streams[1].CodecId);
            foreach (JsonElement item in reference.RootElement.GetProperty("packets").EnumerateArray())
            {
                using MediaPacket packet = (await reader.ReadPacketAsync())!;
                Assert.Equal(item.GetProperty("stream_index").GetInt32(), packet.StreamIndex);
                Assert.Equal(item.GetProperty("pts").GetInt64(), packet.PresentationTime!.Value.Value);
                Assert.Equal(item.GetProperty("dts").GetInt64(), packet.DecodeTime!.Value.Value);
                Assert.Equal(item.GetProperty("duration").GetInt64(), packet.Duration!.Value.Value);
                int offset = int.Parse(item.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                int size = int.Parse(item.GetProperty("size").GetString()!, CultureInfo.InvariantCulture);
                Assert.Equal(bytes.AsSpan(offset, size).ToArray(), packet.Data.ToArray());
                Assert.True(packet.IsKeyFrame);
            }
            Assert.Null(await reader.ReadPacketAsync());
            Assert.Null(await reader.ReadPacketAsync());
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PublicAudiovisualOperationDecodesAndEncodesBothTracksWithoutContainerSpecificExecution()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-pcm.avi"));
            await using AviTestSource source = new(input);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory()).AddAudioDecoder(new PcmDecoderFactory())
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024, MemoryPool<byte>.Shared);
            using MemoryStream output = new();
            MediaProcessingResult result = await engine.ProcessAsync(source, output, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), new VideoPreparationOptions { RgbConversion = new(), MaximumWidth = 32 }),
                 new AudioEncodingRequest(1, 1, new AacEncoderFactory())], allocator);
            Assert.Equal(12, result.Tracks[0].Frames);
            Assert.Equal(72000, result.Tracks[1].SamplesPerChannel);
            Assert.True(output.Length > 0 && output.Length % 188 == 0);
            Assert.False(source.Disposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MalformedExtentsIndexAddressesAndAllocationFailuresReleaseAllOwnedStorage()
        {
            byte[] valid = AviTestData.Build(AviIndexKind.Relative);
            for (int length = 0; length < valid.Length; length += 7)
            {
                await using AviTestSource source = new(valid[..length]);
                MediaBufferAllocator allocator = new(1024 * 1024);
                await Assert.ThrowsAsync<InvalidDataException>(async () => await AviReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            foreach (AviIndexKind kind in Enum.GetValues<AviIndexKind>())
            {
                await using AviTestSource source = new(AviTestData.Build(kind));
                MediaBufferAllocator allocator = new(4097);
                await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await AviReader.OpenAsync(source, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            byte[] bad = valid.ToArray();
            int old = bad.AsSpan().IndexOf("idx1"u8);
            BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(old + 16), uint.MaxValue);
            await using AviTestSource damaged = new(bad);
            MediaBufferAllocator budget = new(1024 * 1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await AviReader.OpenAsync(damaged, budget));
            Assert.Equal(0, budget.AllocatedBytes);
        }
    }
}
