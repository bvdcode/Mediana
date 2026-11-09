// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TileFailureTests
    {
        [Theory]
        [InlineData("missing-group")]
        [InlineData("reordered-groups")]
        [InlineData("duplicated-group")]
        [InlineData("missing-header")]
        [InlineData("invalid-header-padding")]
        [InlineData("truncated-obu")]
        public void MalformedGroupsFaultTheDecoderAndReleaseAllOwnedBuffers(string mutation)
        {
            byte[] valid = FirstPacket();
            byte[] malformed = Mutate(valid, mutation);
            MediaBufferAllocator storage = new(1024 * 1024);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            Av1DecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(Stream(), allocator);
            using (MediaPacket packet = Packet(malformed, storage))
            {
                Assert.Throws<InvalidDataException>(() => factory.CreateRandomAccessInspector(Stream()).IsRandomAccess(packet));
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, storage.AllocatedBytes);
            decoder.Reset();
            using (MediaPacket packet = Packet(valid, storage))
            {
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using VideoFrame frame = decoder.Receive().Value;
                Assert.Equal(192, frame.Format.Width);
            }
            decoder.Reset();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, storage.AllocatedBytes);
        }

        [Fact]
        public void MemoryBudgetAndCancellationRemainCallerControlled()
        {
            byte[] encoded = FirstPacket();
            MediaBufferAllocator storage = new(1024 * 1024);
            MediaBufferAllocator budget = new(1);
            using MediaPacket packet = Packet(encoded, storage);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new Av1DecoderFactory().Create(Stream(), budget);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(0, budget.AllocatedBytes);
            decoder.Reset();
            Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, new(true)));
            Assert.Equal(0, budget.AllocatedBytes);
        }

        private static byte[] Mutate(byte[] valid, string mutation)
        {
            List<(int Start, int End, int Payload)> groups = Obus(valid, 4);
            (int headerStart, int headerEnd, _) = Assert.Single(Obus(valid, 3));
            (int firstStart, int firstEnd, _) = groups[0];
            (int lastStart, int lastEnd, _) = groups[^1];
            switch (mutation)
            {
                case "missing-group":
                    return [.. valid.AsSpan(0, lastStart), .. valid.AsSpan(lastEnd)];
                case "reordered-groups":
                    return [.. valid.AsSpan(0, firstStart), .. valid.AsSpan(lastStart, lastEnd - lastStart),
                        .. valid.AsSpan(firstStart, firstEnd - firstStart), .. valid.AsSpan(lastEnd)];
                case "duplicated-group":
                    return [.. valid, .. valid.AsSpan(firstStart, firstEnd - firstStart)];
                case "missing-header":
                    return [.. valid.AsSpan(0, headerStart), .. valid.AsSpan(headerEnd)];
                case "invalid-header-padding":
                    byte[] padding = valid.ToArray();
                    padding[headerEnd - 1] = 0;
                    return padding;
                case "truncated-obu":
                    return valid[..^1];
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }

        private static List<(int Start, int End, int Payload)> Obus(byte[] encoded, int expectedType)
        {
            List<(int Start, int End, int Payload)> result = [];
            Av1ObuReader reader = new(encoded);
            int start = 0;
            while (reader.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == expectedType) { result.Add((start, reader.ByteOffset, reader.ByteOffset - payload.Length)); }
                start = reader.ByteOffset;
            }
            return result;
        }

        private static byte[] FirstPacket() => Av1PictureSequenceTestInput.Packets(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "tilesGroups.obu")))[0].ToArray();

        private static EncodedStreamInfo Stream() => new(1, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 24), [0x81, 0, 12, 0]);

        private static MediaPacket Packet(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(encoded.Length);
            encoded.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(1, buffer);
        }
    }
}
