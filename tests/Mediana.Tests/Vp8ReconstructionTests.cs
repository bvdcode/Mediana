// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Vp8;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Tests
{
    public class Vp8ReconstructionTests
    {
        [Theory]
        [InlineData("motion")]
        [InlineData("odd")]
        [InlineData("split")]
        [InlineData("altref")]
        [InlineData("noise")]
        [InlineData("version1")]
        [InlineData("version2")]
        [InlineData("version3")]
        public async Task EveryVisiblePictureMatchesIndependentNativeReconstructionAfterReset(string name)
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(Root, name + ".ivf"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(Root, name + ".yuv"));
            int width = BinaryPrimitives.ReadUInt16LittleEndian(ivf.AsSpan(12)), height = BinaryPrimitives.ReadUInt16LittleEndian(ivf.AsSpan(14));
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, System.Buffers.MemoryPool<byte>.Shared);
            Vp8DecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(Stream(), allocator);
            IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(Stream());
            for (int run = 0; run < 2; run++)
            {
                int cursor = 32, output = 0, number = 0, hidden = 0;
                while (cursor < ivf.Length)
                {
                    int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(cursor))); cursor += 12;
                    ReadOnlyMemory<byte> encoded = ivf.AsMemory(cursor, length); cursor += length;
                    Vp8UncompressedHeader tag = Vp8UncompressedHeader.Parse(encoded.Span);
                    using MediaPacket packet = Packet(encoded.Span, number);
                    Assert.Equal(tag.KeyFrame, inspector.IsRandomAccess(packet));
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    if (!tag.ShowFrame) { Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status); hidden++; continue; }
                    Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                    using VideoFrame frame = decoder.Receive().Value;
                    Assert.Equal(packet.PresentationTime, frame.Timestamp); Assert.Equal(packet.Duration, frame.Duration);
                    Assert.Equal(width, frame.Format.Width); Assert.Equal(height, frame.Format.Height);
                    Assert.Same(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                    for (int plane = 0; plane < 3; plane++)
                    {
                        for (int y = 0; y < frame.Planes[plane].Height; y++)
                        {
                            ReadOnlySpan<byte> pixels = frame.GetRow(plane, y);
                            Assert.True(pixels.SequenceEqual(expected.AsSpan(output, pixels.Length)), $"{name}, frame {number}, plane {plane}, row {y}");
                            output += pixels.Length;
                        }
                    }
                    number++;
                }
                Assert.Equal(expected.Length, output);
                if (name == "altref") { Assert.Equal(4, hidden); }
                decoder.CompleteInput(); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                decoder.Reset(); Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task ReferenceLeasesShareTheOutputAllocationAndOutliveResetAndGeometryChanges()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(Root, "motion.ivf"));
            byte[] odd = await File.ReadAllBytesAsync(Path.Combine(Root, "odd.ivf"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Vp8DecoderFactory().Create(Stream(), allocator);
            using MediaPacket first = FirstPacket(ivf);
            decoder.TrySend(first);
            using VideoFrame retained = decoder.Receive().Value;
            byte[] snapshot = retained.Data.ToArray();
            Assert.Equal(64 * 48 * 3 / 2 + 12, allocator.AllocatedBytes);
            using MediaPacket changed = FirstPacket(odd);
            decoder.TrySend(changed);
            using VideoFrame changedFrame = decoder.Receive().Value;
            Assert.Equal(65, changedFrame.Format.Width); Assert.Equal(49, changedFrame.Format.Height);
            Assert.Equal(snapshot, retained.Data.ToArray());
            decoder.Reset();
            Assert.Equal(retained.Data.Length + changedFrame.Data.Length, allocator.AllocatedBytes);
            decoder.Dispose();
            Assert.Equal(snapshot, retained.Data.ToArray());
            retained.Dispose(); changedFrame.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MalformedInputBudgetsAndCancellationReleaseReferencesAndRequireReset()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(Root, "motion.ivf"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Vp8DecoderFactory().Create(Stream(), allocator);
            using MediaPacket first = FirstPacket(ivf);
            decoder.TrySend(first); decoder.Receive().Value.Dispose();
            using MediaPacket truncated = Packet([0, 0, 0]);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(truncated));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.TrySend(first, new(true)));
            Assert.Equal(0, allocator.AllocatedBytes); decoder.Reset();
            decoder.TrySend(first);
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Receive(new(true)));
            decoder.Receive().Value.Dispose(); decoder.Reset(); Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(4608);
            using IMediaCodec<MediaPacket, VideoFrame> bounded = new Vp8DecoderFactory().Create(Stream(), small);
            Assert.Throws<MediaMemoryLimitException>(() => bounded.TrySend(first));
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Fact]
        public async Task IndependentRestartRejectsInterPicturesAndRetainsExplicitSourceGeometryAndColor()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(Root, "motion.ivf"));
            int firstLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)));
            int second = 44 + firstLength, secondLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(second)));
            using MediaPacket inter = Packet(ivf.AsSpan(second + 12, secondLength));
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "vp8", StreamFormats.Vp8, new(1, 12),
                aspectRatioDescription: new(new(4, 3)), colorDescription: new CicpColorDescription(2, 2, 6, true));
            Vp8DecoderFactory factory = new();
            Assert.False(factory.CreateRandomAccessInspector(stream).IsRandomAccess(inter));
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(inter)); decoder.Reset();
            using MediaPacket first = FirstPacket(ivf); decoder.TrySend(first);
            using VideoFrame frame = decoder.Receive().Value;
            Assert.Equal(new SampleAspectRatio(4, 3), frame.Format.SampleAspectRatio);
            Assert.Equal(ColorRange.Full, frame.Format.GetColorInterpretation().Range);
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "Vp8");
        private static EncodedStreamInfo Stream() => new(0, MediaKind.Video, "vp8", StreamFormats.Vp8, new(1, 12));
        private static MediaPacket FirstPacket(byte[] ivf) => Packet(ivf.AsSpan(44, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)))));
        private static MediaPacket Packet(ReadOnlySpan<byte> bytes, int number = 0)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length); bytes.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer, new(number, new(1, 12)), new(number, new(1, 12)), new(1, new(1, 12)));
        }
    }
}
