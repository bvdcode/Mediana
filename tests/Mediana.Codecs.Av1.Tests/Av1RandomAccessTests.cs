// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1RandomAccessTests
    {
        [Theory]
        [InlineData("interResidual", false)]
        [InlineData("interPrimary", true)]
        [InlineData("interOrdered", false)]
        public void OnlyClosedKeyPicturesRestartWithoutReferenceState(string fixture, bool configured)
        {
            byte[] encoded = Read(fixture + ".obu");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            EncodedStreamInfo stream = Stream(encoded, configured);
            MediaBufferAllocator allocator = new(1024 * 1024);
            IRandomAccessInspector inspector = new Av1DecoderFactory().CreateRandomAccessInspector(stream);
            for (int index = units.Count - 1; index >= 0; index--)
            {
                using MediaPacket packet = Packet(configured ? FrameOnly(units[index]).Span : units[index].Span, allocator);
                Assert.Equal(index == 0, inspector.IsRandomAccess(packet));
                if (index == 0)
                {
                    using IMediaCodec<MediaPacket, VideoFrame> decoder = new Av1DecoderFactory().Create(stream, allocator);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    decoder.Receive().Value.Dispose();
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void HiddenClosedKeyAndDisplayShareOneColdAccessUnit()
        {
            byte[] encoded = Read("hidden.obu");
            EncodedStreamInfo stream = Stream(encoded, true);
            MediaBufferAllocator allocator = new(1024 * 1024);
            IRandomAccessInspector inspector = new Av1DecoderFactory().CreateRandomAccessInspector(stream);
            using (MediaPacket packet = Packet(encoded, allocator))
            {
                Assert.True(inspector.IsRandomAccess(packet));
                using IMediaCodec<MediaPacket, VideoFrame> decoder = new Av1DecoderFactory().Create(stream, allocator);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                decoder.Receive().Value.Dispose();
            }
            using (MediaPacket display = Packet([0x1a, 1, 0x88], allocator))
            {
                Assert.False(inspector.IsRandomAccess(display));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MissingConfigurationTruncationWrongStreamAndCancellationAreExplicit()
        {
            byte[] encoded = Read("interPrimary.obu");
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            IRandomAccessInspector inspector = new Av1DecoderFactory().CreateRandomAccessInspector(Stream(encoded, false));
            using (MediaPacket missing = Packet(FrameOnly(units[0]).Span, allocator))
            {
                Assert.False(inspector.IsRandomAccess(missing));
            }
            using (MediaPacket truncated = Packet([0x32, 2, 0], allocator))
            {
                Assert.Throws<InvalidDataException>(() => inspector.IsRandomAccess(truncated));
            }
            using (MediaPacket valid = Packet(units[0].Span, allocator))
            {
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => inspector.IsRandomAccess(valid, cancellation.Token));
                using MediaBuffer borrowed = valid.RetainData();
                using MediaPacket wrong = new(9, borrowed, valid.PresentationTime, duration: valid.Duration);
                Assert.Throws<ArgumentException>(() => inspector.IsRandomAccess(wrong));
                Assert.True(inspector.IsRandomAccess(valid));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FrameIdentifierWidthsOverSixteenAreInvalidBitstreams()
        {
            Av1ObuReader obus = new(Read("interRepeat.obu"));
            byte[] sequence = [];
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1) { sequence = payload.ToArray(); break; }
            }
            Av1BitReader bits = new(sequence);
            bits.Read(5);
            Assert.False(bits.Flag());
            Assert.False(bits.Flag());
            Assert.Equal(0, bits.Read(5));
            bits.Read(12);
            int level = bits.Read(5);
            if (level > 7) { bits.Read(1); }
            int widthBits = bits.Read(4) + 1;
            int heightBits = bits.Read(4) + 1;
            bits.Read(widthBits);
            bits.Read(heightBits);
            Assert.True(bits.Flag());
            int position = sequence.Length * 8 - bits.RemainingBits;
            for (int index = 0; index < 4; index++)
            {
                int bit = position + index;
                sequence[bit >> 3] |= (byte)(128 >> (bit & 7));
            }
            Assert.Throws<InvalidDataException>(() => Av1SequenceHeader.Parse(sequence));
        }

        private static EncodedStreamInfo Stream(ReadOnlySpan<byte> encoded, bool configured) => new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Av1CodecTests.Configuration(encoded, configured));

        private static ReadOnlyMemory<byte> FrameOnly(ReadOnlyMemory<byte> encoded)
        {
            Av1ObuReader obus = new(encoded.Span);
            int previous = 0;
            while (obus.TryRead(out int type, out _))
            {
                if (type == 6) { return encoded[previous..obus.ByteOffset]; }
                previous = obus.ByteOffset;
            }
            throw new InvalidDataException("The fixture has no frame OBU.");
        }

        private static MediaPacket Packet(ReadOnlySpan<byte> data, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(data.Length);
            data.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(3, buffer, new(0, new(1, 25)), duration: new(1, new(1, 25)));
        }

        private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));
    }
}
