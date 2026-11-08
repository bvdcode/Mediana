// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SegmentationFailureTests
    {
        [Fact]
        public void UnchangedMapSharesItsBudgetedStorageAcrossReferencePictures()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "segmentationFeatures.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Av1CodecTests.Configuration(encoded, true));
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                using MediaPacket key = Packet(units[0].Span, input);
                using MediaPacket inter = Packet(units[1].Span, input);
                codec.TrySend(key);
                codec.Receive().Value.Dispose();
                codec.TrySend(inter);
                codec.Receive().Value.Dispose();
                Assert.Equal(2 * (16 * 16 + Av1DefaultCdfs.Count * sizeof(ushort)) + 4 * 4, allocator.AllocatedBytes);
                codec.Reset();
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Equal(0, input.AllocatedBytes);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ScopedMapAndSnapshotFailuresReleaseEverySharedReferenceAndReset(bool snapshot)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "segmentationFeatures.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int samples = 16 * 16;
            int map = 4 * 4;
            int cdf = Av1DefaultCdfs.Count * sizeof(ushort);
            int retained = samples + map + cdf;
            int limit = retained + samples + map + (snapshot ? cdf : 0) - 1;
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator root = new(1024 * 1024);
            MediaBufferAllocator allocator = root.CreateScope(limit);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Av1CodecTests.Configuration(encoded, true));
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                using MediaPacket key = Packet(units[0].Span, input);
                using MediaPacket inter = Packet(units[3].Span, input);
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
                Assert.Equal(retained, allocator.AllocatedBytes);
                MediaMemoryLimitException failure = Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(inter));
                Assert.Equal(snapshot ? cdf : map, failure.RequestedBytes);
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Equal(0, root.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, root.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        private static MediaPacket Packet(ReadOnlySpan<byte> bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(bytes.Length);
            bytes.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(0, buffer, new(0, new(1, 25)), duration: new(1, new(1, 25)));
        }
    }
}
