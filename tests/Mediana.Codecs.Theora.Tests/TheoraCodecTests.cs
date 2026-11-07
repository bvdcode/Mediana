// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Security.Cryptography;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraCodecTests
    {
        [Theory]
        [InlineData("main420")]
        [InlineData("chroma422")]
        [InlineData("chroma444")]
        [InlineData("motion")]
        [InlineData("noise")]
        [InlineData("odd420")]
        [InlineData("fractional")]
        public async Task PublicCodecRetainsIndependentFramesAndExactTimestampsAcrossReset(string name)
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync(name);
            TheoraIdentification info = TheoraIdentification.Read(packets[0]);
            TimeBase clock = new((int)info.FrameRateDenominator, (int)info.FrameRateNumerator);
            EncodedStreamInfo stream = new(2, MediaKind.Video, "theora", StreamFormats.Theora, clock, TheoraTestInput.Configuration(packets));
            string[] expected = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name + ".framehash")))
                .Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim().ToUpperInvariant()).ToArray();
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, MemoryPool<byte>.Shared);
            TheoraDecoderFactory factory = new(); IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(stream);
            VideoFrame? retained = null;
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    decoder.Reset();
                    for (int i = 3; i < packets.Count; i++)
                    {
                        using MediaPacket packet = Packet(allocator, packets[i], stream, i - 3);
                        Assert.Equal((packets[i][0] & 0x40) == 0, inspector.IsRandomAccess(packet));
                        Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                        Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                        using VideoFrame frame = decoder.Receive().Value;
                        Assert.Equal(packet.PresentationTime, frame.Timestamp); Assert.Equal(packet.Duration, frame.Duration);
                        Assert.Equal(expected[i - 3], Hash(frame, hash)); retained ??= frame.Retain();
                    }
                    decoder.CompleteInput(); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                }
                decoder.Dispose(); Assert.Equal(expected[0], Hash(retained!, hash));
            }
            finally { retained?.Dispose(); decoder.Dispose(); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task DuplicatePacketsSharePixelsAndFaultsReleaseWorkspacesUntilReset()
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync("main420");
            MediaBufferAllocator allocator = new(4 * 1024 * 1024, MemoryPool<byte>.Shared);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "theora", StreamFormats.Theora, new(1,12), TheoraTestInput.Configuration(packets));
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new TheoraDecoderFactory().Create(stream, allocator);
            using (MediaPacket packet = Packet(allocator, [], stream, 0))
            { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.Throws<InvalidOperationException>(() => decoder.Receive()); decoder.Reset();
            using (MediaPacket packet = Packet(allocator, packets[3], stream, 0)) { decoder.TrySend(packet); }
            using (VideoFrame first = decoder.Receive().Value)
            {
                using (MediaPacket packet = Packet(allocator, [], stream, 19))
                {
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    using VideoFrame duplicate = decoder.Receive().Value;
                    Assert.True(first.Data.Equals(duplicate.Data)); Assert.Equal(packet.PresentationTime, duplicate.Timestamp);
                }
                using (MediaPacket packet = Packet(allocator, packets[4], stream, 20))
                { Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, new(true))); }
                Assert.Throws<InvalidOperationException>(() => decoder.Receive()); decoder.Reset();
                using (MediaPacket packet = Packet(allocator, packets[4], stream, 21))
                { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            }
            decoder.Reset(); Assert.Equal(0, allocator.AllocatedBytes);
            using (MediaPacket packet = Packet(allocator, [0x80], stream, 0))
            { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            decoder.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(2048)]
        [InlineData(30000)]
        public async Task AllocatorFailureReturnsEveryPreviouslyAcquiredBuffer(int budget)
        {
            List<byte[]> packets = await TheoraTestInput.ReadPacketsAsync("main420");
            MediaBufferAllocator input = new(1024 * 1024), allocator = new(budget);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "theora", StreamFormats.Theora, new(1,12), TheoraTestInput.Configuration(packets));
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new TheoraDecoderFactory().Create(stream, allocator);
            using MediaPacket packet = Packet(input, packets[3], stream, 0);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet)); Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static MediaPacket Packet(MediaBufferAllocator allocator, ReadOnlySpan<byte> data, EncodedStreamInfo stream, int time)
        {
            using WritableMediaBuffer writable = allocator.Allocate(Math.Max(1, data.Length)); data.CopyTo(writable.Span);
            using MediaBuffer complete = writable.Publish(); using MediaBuffer view = complete.Slice(0, data.Length);
            MediaTimestamp timestamp = new(time, stream.TimeBase);
            return new(stream.StreamIndex, view, timestamp, timestamp, new(1, stream.TimeBase));
        }
        private static string Hash(VideoFrame frame, IncrementalHash hash)
        {
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            { for (int row = 0; row < frame.Planes[plane].Height; row++) { hash.AppendData(frame.GetRow(plane, row)); } }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
