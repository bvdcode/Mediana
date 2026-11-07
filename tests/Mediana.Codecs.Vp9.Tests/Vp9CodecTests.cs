// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9CodecTests
    {
        [Theory]
        [InlineData("main8")]
        [InlineData("resize")]
        [InlineData("main10")]
        [InlineData("high8")]
        [InlineData("high12")]
        [InlineData("main12")]
        [InlineData("chroma422")]
        [InlineData("chroma440")]
        [InlineData("rgb")]
        [InlineData("lossless")]
        [InlineData("resilient")]
        [InlineData("tiles")]
        [InlineData("altref")]
        [InlineData("adaptive")]
        public async Task PublicCodecPreservesAllPicturesAcrossResetAndIndependentFrameLifetimes(string name)
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name + ".ivf"));
            string[] expected = (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Data", name + ".sha256"))).Where(line => !line.StartsWith('#')).Select(line => line.Split(',')[^1].Trim().ToUpperInvariant()).ToArray();
            MediaBufferAllocator allocator = new(128 * 1024 * 1024, MemoryPool<byte>.Shared);
            TimeBase timeBase = new(checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(20))), checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(16))));
            EncodedStreamInfo stream = new(2, MediaKind.Video, "vp9", StreamFormats.Vp9, timeBase, []);
            Vp9DecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
            IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(stream);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            VideoFrame? retained = null;
            try
            {
                for (int trial = 0; trial < 2; trial++)
                {
                    decoder.Reset(); int visible = 0;
                    for (int offset = 32; offset < ivf.Length;)
                    {
                        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(offset)));
                        MediaTimestamp timestamp = new(checked((long)BinaryPrimitives.ReadUInt64LittleEndian(ivf.AsSpan(offset + 4))), timeBase);
                        offset += 12;
                        using WritableMediaBuffer writable = allocator.Allocate(length); ivf.AsSpan(offset, length).CopyTo(writable.Span); offset += length;
                        using MediaBuffer buffer = writable.Publish();
                        using MediaPacket packet = new(2, buffer, timestamp, timestamp, new(1, timeBase));
                        if (visible == 0) { Assert.True(inspector.IsRandomAccess(packet)); }
                        Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                        CodecOutput<VideoFrame> result = decoder.Receive();
                        if (result.Status == CodecOutputStatus.InputRequired) { continue; }
                        Assert.Equal(CodecOutputStatus.Available, result.Status);
                        using VideoFrame frame = result.Value;
                        Assert.Equal(timestamp, frame.Timestamp); Assert.Equal(new MediaDuration(1, timeBase), frame.Duration);
                        PlanarVideoPixelFormat pixels = Assert.IsType<PlanarVideoPixelFormat>(frame.Format.PixelFormat);
                        Assert.Equal(pixels.BitDepth == 8 ? 1 : 2, pixels.Planes[0].BytesPerElement);
                        for (int plane = 0; plane < frame.Planes.Count; plane++)
                        { for (int y = 0; y < frame.Planes[plane].Height; y++) { hash.AppendData(frame.GetRow(plane, y)); } }
                        Assert.Equal(expected[visible++], Convert.ToHexString(hash.GetHashAndReset()));
                        if (retained is null) { retained = frame.Retain(); }
                    }
                    Assert.Equal(expected.Length, visible); decoder.CompleteInput(); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                }
                decoder.Dispose();
                for (int plane = 0; plane < retained!.Planes.Count; plane++)
                { for (int y = 0; y < retained.Planes[plane].Height; y++) { hash.AppendData(retained.GetRow(plane, y)); } }
                Assert.Equal(expected[0], Convert.ToHexString(hash.GetHashAndReset()));
            }
            finally { retained?.Dispose(); decoder.Dispose(); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ShowExistingRetainsReferencePixelsAndBackpressureDoesNotConsumeInput()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "main8.ivf"));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)));
            MediaBufferAllocator allocator = new(16 * 1024 * 1024, MemoryPool<byte>.Shared);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "vp9", StreamFormats.Vp9, new(1, 8), []);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Vp9DecoderFactory().Create(stream, allocator);
            using (WritableMediaBuffer writable = allocator.Allocate(length))
            {
                ivf.AsSpan(44, length).CopyTo(writable.Span); using MediaBuffer buffer = writable.Publish();
                using MediaPacket packet = new(0, buffer, new(0, stream.TimeBase));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            }
            VideoFrame first = decoder.Receive().Value;
            using (first)
            using (WritableMediaBuffer writable = allocator.Allocate(1))
            {
                writable.Span[0] = 0x88; using MediaBuffer buffer = writable.Publish();
                using MediaPacket packet = new(0, buffer, new(17, stream.TimeBase), duration: new(3, stream.TimeBase));
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                using VideoFrame repeated = decoder.Receive().Value;
                Assert.True(first.Data.Equals(repeated.Data)); Assert.Equal(packet.PresentationTime, repeated.Timestamp); Assert.Equal(packet.Duration, repeated.Duration);
                decoder.Dispose(); first.Dispose(); Assert.NotEmpty(repeated.GetRow(0, 0).ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidInputsFaultUntilResetAndCancellationReleasesReferences()
        {
            MediaBufferAllocator allocator = new(1024 * 1024, MemoryPool<byte>.Shared);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "vp9", StreamFormats.Vp9, new(1, 24), []);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Vp9DecoderFactory().Create(stream, allocator);
            using (WritableMediaBuffer writable = allocator.Allocate(1))
            {
                using MediaBuffer buffer = writable.Publish(); using MediaPacket packet = new(0, buffer);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive()); decoder.Reset();
                Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, new(true)));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive()); decoder.Reset();
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            }
            decoder.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
