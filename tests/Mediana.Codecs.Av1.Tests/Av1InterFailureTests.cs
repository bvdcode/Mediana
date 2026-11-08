// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1InterFailureTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void InterPictureBudgetFailureReleasesReferencesAndEveryWorkspace(int allocation)
        {
            byte[] encoded = Read("interRepeat.obu");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int samples = 48 * 40 * 3 / 2;
            int snapshot = Av1DefaultCdfs.Count * sizeof(ushort);
            int motions = 12 * 10 * Unsafe.SizeOf<Av1MotionBlock>();
            int contexts = (12 + Av1TileDecoder.LeftContexts) * 2 * Unsafe.SizeOf<Av1BlockContext>();
            int retained = samples + snapshot;
            long limit = allocation switch
            {
                0 => retained,
                1 => retained + samples,
                2 => retained + samples + motions,
                3 => retained + samples + motions + contexts,
                4 => retained + samples + snapshot - 1,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator pixels = parent.CreateScope(limit);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), pixels))
            {
                using MediaPacket key = Packet(units[0].Span, input);
                using MediaPacket inter = Packet(units[1].Span, input);
                codec.TrySend(key);
                codec.Receive().Value.Dispose();
                Assert.Equal(retained, pixels.AllocatedBytes);
                Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(inter));
                Assert.Equal(0, pixels.AllocatedBytes);
                Assert.Equal(0, parent.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, pixels.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        [Fact]
        public void DeblockingMetadataFailureHonorsTheScopedBudgetAndReleasesReferences()
        {
            byte[] encoded = Read("interDeblocked8.obu");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int samples = 48 * 40 * 3 / 2;
            int snapshot = Av1DefaultCdfs.Count * sizeof(ushort);
            int motions = 12 * 10 * Unsafe.SizeOf<Av1MotionBlock>();
            int metadata = 12 * 10 * Unsafe.SizeOf<Av1FilterBlock>();
            int retained = samples + snapshot;
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator pixels = parent.CreateScope(retained + samples + motions + metadata - 1);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), pixels))
            {
                using MediaPacket key = Packet(units[0].Span, input);
                using MediaPacket inter = Packet(units[1].Span, input);
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
                Assert.Equal(retained, pixels.AllocatedBytes);
                MediaMemoryLimitException error = Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(inter));
                Assert.Equal(metadata, error.RequestedBytes);
                Assert.Equal(0, pixels.AllocatedBytes);
                Assert.Equal(0, parent.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, pixels.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        [Fact]
        public void EveryTruncatedInterPacketFaultsWithoutLeakingReferences()
        {
            byte[] encoded = Read("interRepeat.obu");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator pixels = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), pixels))
            {
                using MediaPacket key = Packet(units[0].Span, input);
                for (int length = 0; length < units[1].Length; length++)
                {
                    codec.Reset();
                    codec.TrySend(key);
                    codec.Receive().Value.Dispose();
                    using MediaPacket truncated = Packet(units[1].Span[..length], input);
                    Assert.Throws<InvalidDataException>(() => codec.TrySend(truncated));
                    Assert.Equal(0, pixels.AllocatedBytes);
                }
            }
            Assert.Equal(0, pixels.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        [Fact]
        public void UnsupportedCompoundBlockFailsAfterCorrectlyDecodedPrefixAndReleasesReferences()
        {
            byte[] encoded = Read("interCompound.obu");
            byte[] expected = Read("interCompound.yuv");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator pixels = new(1024 * 1024);
            int decoded = 0;
            bool rejected = false;
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), pixels))
            {
                foreach (ReadOnlyMemory<byte> unit in units)
                {
                    using MediaPacket packet = Packet(unit.Span, input);
                    Exception? error = Record.Exception(() => codec.TrySend(packet));
                    if (error is not null)
                    {
                        Assert.IsType<NotSupportedException>(error);
                        Assert.Equal("AV1 compound block prediction is not implemented.", error.Message);
                        Assert.Equal(0, pixels.AllocatedBytes);
                        Assert.Throws<InvalidOperationException>(() => codec.Receive());
                        rejected = true;
                        break;
                    }
                    using VideoFrame frame = codec.Receive().Value;
                    int offset = decoded * 48 * 40 * 3 / 2;
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                            Assert.True(expected.AsSpan(offset, actual.Length).SequenceEqual(actual));
                            offset += actual.Length;
                        }
                    }
                    decoded++;
                }
                Assert.True(rejected);
                Assert.True(decoded >= 2);
                codec.Reset();
                using MediaPacket key = Packet(units[0].Span, input);
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, input.AllocatedBytes);
            Assert.Equal(0, pixels.AllocatedBytes);
        }

        private static EncodedStreamInfo Stream(ReadOnlySpan<byte> encoded) =>
            new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Av1CodecTests.Configuration(encoded, true));

        private static MediaPacket Packet(ReadOnlySpan<byte> bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(Math.Max(1, bytes.Length));
            bytes.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            using MediaBuffer payload = buffer.Slice(0, bytes.Length);
            return new(0, payload, new(0, new(1, 25)), duration: new(1, new(1, 25)));
        }

        private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", file));
    }
}
