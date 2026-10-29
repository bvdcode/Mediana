// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class Mp4ReaderTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", 17, false, false)]
        [InlineData("avc-tail.mov", 3, false, false)]
        [InlineData("avc-tail.mov", 11, true, false)]
        [InlineData("avc-tail.mov", 7, true, true)]
        public async Task RealFilesMatchIndependentPacketBytesAndTiming(string name, int maxRead,
            bool wideOffsets, bool wideHeaders)
        {
            byte[] data = await Mp4TestData.ReadAsync(name);
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Mp4TestData.PathFor(name + ".json")));
            Assert.Equal(reference.RootElement.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(data)));
            if (wideHeaders)
            {
                data = Mp4TestData.Use64BitTimeHeaders(data);
            }
            if (wideOffsets)
            {
                data = Mp4TestData.Use64BitChunkOffsets(data);
            }
            JsonElement[] packets = reference.RootElement.GetProperty("packets").EnumerateArray().ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, maxRead);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long firstPayload = packets.Min(packet => long.Parse(packet.GetProperty("pos").GetString()!));
            long payloadEnd = packets.Max(packet => long.Parse(packet.GetProperty("pos").GetString()!) +
                int.Parse(packet.GetProperty("size").GetString()!));
            Assert.All(source.Reads, range => Assert.True(range.Offset + range.Length <= firstPayload || range.Offset >= payloadEnd));
            JsonElement[] streams = reference.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert.Equal(streams.Length, reader.Streams.Count);
            foreach (JsonElement expected in streams)
            {
                int index = expected.GetProperty("index").GetInt32();
                Mp4TrackInfo track = reader.Tracks[index];
                Assert.Equal(expected.GetProperty("codec_name").GetString(), track.Stream.CodecId);
                Assert.Equal(expected.GetProperty("codec_tag_string").GetString(), track.SampleEntry);
                Assert.Equal(expected.GetProperty("extradata_size").GetInt32(), track.Stream.Configuration.Length);
                Assert.Equal(expected.GetProperty("extradata_hash").GetString(), Hash(track.Stream.Configuration.Span));
                Assert.Equal(expected.GetProperty("duration_ts").GetInt64(), track.MediaDuration!.Value.Value);
                Assert.Equal(expected.GetProperty("time_base").GetString(),
                    $"{track.Stream.TimeBase.Numerator}/{track.Stream.TimeBase.Denominator}");
                if (track.Stream.Kind == MediaKind.Video)
                {
                    Assert.Equal(expected.GetProperty("width").GetInt32(), track.Width);
                    Assert.Equal(expected.GetProperty("height").GetInt32(), track.Height);
                }
                Assert.Equal(packets.Count(packet => packet.GetProperty("stream_index").GetInt32() == index), track.SampleCount);
            }
            Dictionary<int, Queue<JsonElement>> expectedPackets = packets.GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                .ToDictionary(group => group.Key, group => new Queue<JsonElement>(group));
            MediaTimestamp? previous = null;
            int count = 0;
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    JsonElement expected = expectedPackets[packet.StreamIndex].Dequeue();
                    Assert.Equal(expected.GetProperty("pts").GetInt64(), packet.PresentationTime!.Value.Value);
                    Assert.Equal(expected.GetProperty("dts").GetInt64(), packet.DecodeTime!.Value.Value);
                    Assert.Equal(expected.GetProperty("duration").GetInt64(), packet.Duration!.Value.Value);
                    Assert.Equal(int.Parse(expected.GetProperty("size").GetString()!), packet.Data.Length);
                    Assert.Equal(expected.GetProperty("data_hash").GetString(), Hash(packet.Data.Span));
                    Assert.Equal(expected.GetProperty("flags").GetString()!.StartsWith('K'), packet.IsKeyFrame);
                    if (previous is not null)
                    {
                        Assert.True(packet.DecodeTime.Value.CompareTo(previous.Value) >= 0);
                    }
                    previous = packet.DecodeTime;
                    using MediaPacket retained = packet.Retain();
                    Assert.Equal(packet.IsKeyFrame, retained.IsKeyFrame);
                    count++;
                }
            }
            Assert.Equal(packets.Length, count);
            Assert.All(expectedPackets.Values, Assert.Empty);
            Assert.Null(await reader.ReadPacketAsync());
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task EditListAndMovieDurationRemainDistinctFromMediaTimes()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(new MediaDuration(1600, new TimeBase(1, 1000)), reader.Duration);
            Assert.Equal(2, reader.Tracks.Count);
            Assert.Equal(4096, Assert.Single(reader.Tracks[0].Edits).MediaTime);
            Assert.Equal(1024, Assert.Single(reader.Tracks[1].Edits).MediaTime);
            Assert.Equal(65536, reader.Tracks[0].Edits[0].Rate);
            Assert.Equal(1, reader.Tracks[1].DeclaredAudioChannels);
            Assert.Equal(48000, reader.Tracks[1].DeclaredAudioSampleRate);
            using MediaPacket? first = await reader.ReadPacketAsync();
            Assert.NotNull(first);
            Assert.Equal(4096, first.PresentationTime!.Value.Value);
        }

        [Fact]
        public async Task CancelledPacketReadReleasesItsBufferAndCanBeRetried()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 17);
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            long metadataBytes = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            source.AfterRead = cancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPacketAsync(cancellation.Token));
            Assert.Equal(metadataBytes, allocator.AllocatedBytes);
            source.AfterRead = null;
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(0, first.DecodeTime!.Value.Value);
            await reader.DisposeAsync();
            Assert.Equal(first.Data.Length, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            first.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reader.ReadPacketAsync());
        }

        [Fact]
        public async Task EnginePreservesRandomAccessThroughProbeAndReportsMissingDecoder()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            using MemoryStream stream = new(data);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await source.ReadAsync(new byte[1]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).Build();
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await engine.OpenVideoAsync(source, 0));
            Assert.Contains("No decoder supports codec 'h264'", error.Message);
            Assert.True(stream.CanRead);
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public async Task FactoryRequiresRandomAccessWithoutTakingOwnership()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            Mp4ReaderFactory factory = new();
            Assert.True(factory.CanRead(data.AsSpan(0, factory.ProbeLength)));
            Assert.False(factory.CanRead("unknown"u8));
            await using ShortReadSource source = new(data);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await factory.OpenAsync(source, allocator));
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static string Hash(ReadOnlySpan<byte> data) => "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(data));
    }
}
