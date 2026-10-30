// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Streaming.Hls;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.Tests
{
    public class Mp3TransportTests
    {
        [Theory]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task RealRemuxPreservesEveryCompressedFrameAndTimestamp(string file)
        {
            await MpegTsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);
            await MpegTsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file, Mp4TimestampMode.LinearMovie);
        }

        [Theory]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task Mp4ConfigurationReadsOnlyTheHeaderAndDoesNotAdvancePackets(string file)
        {
            byte[] input = await File.ReadAllBytesAsync(Mp4TestData.PathFor(file));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(input, maxRead: 3);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                EncodedStreamInfo stream = reader.Streams[1];
                Assert.Equal("mp3", stream.CodecId);
                Assert.Equal(4, stream.Configuration.Length);
                Assert.Equal(stream.Configuration.ToArray(), reader.Tracks[1].Stream.Configuration.ToArray());
                Mp4SampleInfo first = reader.GetSampleInfo(1, 0);
                Assert.Equal(4, source.Reads.Where(read => read.Offset >= first.Offset && read.Offset < first.Offset + 4).Sum(read => read.Length));
                using MediaPacket packet = await reader.ReadPacketAtAsync(1, 0);
                Assert.Equal(stream.Configuration.ToArray(), packet.Data[..4].ToArray());
                Assert.Equal(first.Size, packet.Data.Length);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData("avc-mp3-44.mp4")]
        [InlineData("avc-mp3-22.mp4")]
        public async Task EveryMp4PacketMatchesIndependentHashesAndMediaTimes(string file)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Mp4TestData.PathFor(file + ".json")));
            Dictionary<int, Queue<JsonElement>> expected = manifest.RootElement.GetProperty("packets").EnumerateArray()
                .GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                .ToDictionary(group => group.Key, group => new Queue<JsonElement>(group));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await File.ReadAllBytesAsync(Mp4TestData.PathFor(file)));
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        JsonElement reference = expected[packet.StreamIndex].Dequeue();
                        Assert.Equal(reference.GetProperty("pts").GetInt64(), packet.PresentationTime!.Value.Value);
                        Assert.Equal(reference.GetProperty("dts").GetInt64(), packet.DecodeTime!.Value.Value);
                        Assert.Equal(reference.GetProperty("duration").GetInt64(), packet.Duration!.Value.Value);
                        Assert.Equal(reference.GetProperty("size").GetString(), packet.Data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        string hash = "SHA256:" + Convert.ToHexString(SHA256.HashData(packet.Data.Span)).ToLowerInvariant();
                        Assert.Equal(reference.GetProperty("data_hash").GetString(), hash);
                    }
                }
                Assert.All(expected.Values, Assert.Empty);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FailedOrCancelledHeaderInspectionReleasesMetadataAndBorrowsTheSource()
        {
            byte[] original = await File.ReadAllBytesAsync(Mp4TestData.PathFor("avc-mp3-44.mp4"));
            int offset;
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using (TrackingRandomAccessSource input = new(original))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(input, allocator))
            {
                offset = checked((int)reader.GetSampleInfo(1, 0).Offset);
            }
            byte[] invalid = (byte[])original.Clone();
            invalid[offset] = 0;
            await using TrackingRandomAccessSource damaged = new(invalid);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Mp4Reader.OpenAsync(damaged, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(damaged.IsDisposed);
            await using TrackingRandomAccessSource limited = new(original);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await Mp4Reader.OpenAsync(limited, allocator,
                new Mp4ReadOptions { MaxCodecConfigurationBytes = 3 }));
            Assert.Equal(0, allocator.AllocatedBytes);
            using CancellationTokenSource cancellation = new();
            await using TrackingRandomAccessSource cancelled = new(original, maxRead: 3);
            cancelled.AfterRead = () =>
            {
                if (cancelled.Reads[^1].Offset == offset)
                {
                    cancellation.Cancel();
                }
            };
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await Mp4Reader.OpenAsync(cancelled, allocator,
                cancellationToken: cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(cancelled.IsDisposed);
        }

        [Theory]
        [InlineData(new byte[] { 0xff, 0xfb, 0x90, 0 }, 0x03)]
        [InlineData(new byte[] { 0xff, 0xfa, 0x92, 0 }, 0x03)]
        [InlineData(new byte[] { 0xff, 0xf3, 0x80, 0xc0 }, 0x04)]
        [InlineData(new byte[] { 0xff, 0xf2, 0x82, 0xc0 }, 0x04)]
        public void TransportKeepsFrameBytesIncludingCrcAndPadding(byte[] configuration, byte expectedType)
        {
            Mp3FrameHeader header = Mp3FrameHeader.Read(configuration);
            byte[] frame = new byte[header.FrameBytes];
            new Random(412).NextBytes(frame);
            configuration.CopyTo(frame, 0);
            Mp3TransportFraming framing = new(configuration);
            Assert.Equal(expectedType, framing.StreamType);
            MediaBufferAllocator allocator = new(4096);
            using (MediaBuffer result = framing.Convert(frame, allocator))
            {
                Assert.Equal(frame, result.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidDataException>(() => framing.Convert(frame[..^1], allocator));
            Assert.Throws<InvalidDataException>(() => framing.Convert([.. frame, 0], allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnsupportedVersionAndChangedFormatAreExplicit()
        {
            Assert.Throws<NotSupportedException>(() => new Mp3TransportFraming([0xff, 0xe3, 0x80, 0xc0]));
            Mp3TransportFraming framing = new([0xff, 0xfb, 0x90, 0]);
            byte[] changed = new byte[Mp3FrameHeader.Read([0xff, 0xfb, 0x94, 0]).FrameBytes];
            new byte[] { 0xff, 0xfb, 0x94, 0 }.CopyTo(changed, 0);
            MediaBufferAllocator allocator = new(4096);
            Assert.Throws<NotSupportedException>(() => framing.Convert(changed, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
