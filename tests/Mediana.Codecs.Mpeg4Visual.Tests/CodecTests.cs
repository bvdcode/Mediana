// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

using Mediana.Tests;

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class CodecTests
    {
        [Theory]
        [InlineData("simple")]
        [InlineData("bframes")]
        [InlineData("matrix")]
        [InlineData("quarter")]
        [InlineData("motion")]
        [InlineData("noise")]
        [InlineData("fractional")]
        [InlineData("odd")]
        [InlineData("xvid")]
        public void PublicCodecPreservesDisplayOrderTimingBackpressureAndOutputLeases(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] data = File.ReadAllBytes(Path.Combine(root, name + ".m4v")), expected = File.ReadAllBytes(Path.Combine(root, name + ".yuv"));
            List<byte[]> packets = Mpeg4TestInput.Packets(data);
            int[] order = File.ReadAllLines(Path.Combine(root, name + ".coded-order.txt")).Select(int.Parse).ToArray();
            int[] display = new int[order.Length]; for (int i = 0; i < order.Length; i++) { display[order[i]] = i; }
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, MemoryPool<byte>.Shared);
            EncodedStreamInfo stream = Mpeg4TestInput.Stream(data, false); Mpeg4DecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
            VideoFrame? retained = null;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    decoder.Reset(); int count = 0;
                    for (int i = 0; i < packets.Count; i++)
                    {
                        int step = name == "fractional" ? 1001 : 1;
                        MediaTimestamp? timestamp = pass == 0 ? null : new(display[i] * (long)step + 12345, stream.TimeBase);
                        using MediaPacket packet = Mpeg4TestInput.Packet(allocator, packets[i], timestamp, new(step, stream.TimeBase));
                        Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                        if (i > 0) { Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet)); }
                        CodecOutput<VideoFrame> result = decoder.Receive();
                        while (result.Status == CodecOutputStatus.Available)
                        {
                            using VideoFrame frame = result.Value;
                            Verify(frame, count++, pass); retained ??= frame.Retain(); result = decoder.Receive();
                        }
                        Assert.Equal(CodecOutputStatus.InputRequired, result.Status);
                    }
                    decoder.CompleteInput(); CodecOutput<VideoFrame> final = decoder.Receive();
                    while (final.Status == CodecOutputStatus.Available)
                    { using VideoFrame frame = final.Value; Verify(frame, count++, pass); final = decoder.Receive(); }
                    Assert.Equal(CodecOutputStatus.EndOfStream, final.Status); Assert.Equal(order.Length, count);
                }
                decoder.Dispose(); Mpeg4TestInput.VerifyPixels(retained!, expected.AsSpan(0, expected.Length / order.Length));
            }
            finally { retained?.Dispose(); decoder.Dispose(); }
            Assert.Equal(0, allocator.AllocatedBytes);
            void Verify(VideoFrame frame, int displayIndex, int pass)
            {
                int step = name == "fractional" ? 1001 : 1;
                Assert.Equal(new MediaTimestamp(displayIndex * (long)step + (pass == 0 ? 0 : 12345), stream.TimeBase), frame.Timestamp);
                Assert.Equal(new MediaDuration(step, stream.TimeBase), frame.Duration);
                Assert.Equal(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                Mpeg4TestInput.VerifyPixels(frame, expected.AsSpan(displayIndex * (expected.Length / order.Length), expected.Length / order.Length));
            }
        }
        [Fact]
        public void EncodedPresentationOrderSupportsClocksBeyondInt32()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "fractional.m4v"));
            for (int at = 0; at + 7 <= data.Length; at++)
            {
                if (data[at] != 0 || data[at + 1] != 0 || data[at + 2] != 1 || data[at + 3] != 0xb3) { continue; }
                data[at + 4] = (byte)((23 << 3) | (data[at + 4] & 7));
            }
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            EncodedStreamInfo stream = Mpeg4TestInput.Stream(data, false);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Mpeg4DecoderFactory().Create(stream, allocator);
            int count = 0;
            foreach (byte[] bytes in Mpeg4TestInput.Packets(data))
            {
                using MediaPacket packet = Mpeg4TestInput.Packet(allocator, bytes); decoder.TrySend(packet);
                CodecOutput<VideoFrame> result = decoder.Receive();
                while (result.Status == CodecOutputStatus.Available)
                {
                    using VideoFrame frame = result.Value;
                    Assert.Equal(23L * 3600 * 30000 + count++ * 1001L, frame.Timestamp!.Value.Value); result = decoder.Receive();
                }
            }
            decoder.CompleteInput(); CodecOutput<VideoFrame> final = decoder.Receive();
            while (final.Status == CodecOutputStatus.Available)
            {
                using VideoFrame frame = final.Value;
                Assert.Equal(23L * 3600 * 30000 + count++ * 1001L, frame.Timestamp!.Value.Value); final = decoder.Receive();
            }
            Assert.Equal(30, count); decoder.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }
        [Theory]
        [InlineData(64)]
        [InlineData(15000)]
        [InlineData(20000)]
        [InlineData(12000)]
        public void FailedAllocationsReleaseEveryPreviouslyAcquiredBuffer(int limit)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "simple.m4v"));
            MediaBufferAllocator allocator = new(limit), input = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Mpeg4DecoderFactory().Create(Mpeg4TestInput.Stream(data, true), allocator);
            using MediaPacket packet = Mpeg4TestInput.Packet(input, Mpeg4TestInput.Packets(data)[0]);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet)); Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
        }
        [Fact]
        public void CancelledInputAndTruncatedSyntaxReleaseStorageAndResetRequiresIntra()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "simple.m4v")); List<byte[]> packets = Mpeg4TestInput.Packets(data);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Mpeg4DecoderFactory().Create(Mpeg4TestInput.Stream(data, true), allocator);
            using (MediaPacket packet = Mpeg4TestInput.Packet(allocator, packets[0])) { decoder.TrySend(packet); }
            using (MediaPacket packet = Mpeg4TestInput.Packet(allocator, packets[1]))
            { Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, new(true))); }
            Assert.Equal(0, allocator.AllocatedBytes); decoder.Reset();
            using (MediaPacket packet = Mpeg4TestInput.Packet(allocator, packets[1])) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            Assert.Equal(0, allocator.AllocatedBytes); decoder.Reset();
            using (MediaPacket packet = Mpeg4TestInput.Packet(allocator, [0,0,1,0xb6])) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
