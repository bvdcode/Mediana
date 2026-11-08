// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ReferenceTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HiddenKeyframeAndReferenceDisplayMatchIndependentPixels(bool separatePackets)
        {
            byte[] encoded = Read("hidden.obu");
            byte[] expected = Read("hidden.yuv");
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator pixels = new(1024 * 1024);
            EncodedStreamInfo stream = Stream(encoded);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, pixels))
            {
                if (separatePackets)
                {
                    IReadOnlyList<ReadOnlyMemory<byte>> packets = Av1SequencePictureTests.AccessUnits(encoded);
                    Assert.Equal(2, packets.Count);
                    using (MediaPacket hidden = Packet(packets[0].Span, input))
                    {
                        Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(hidden));
                        Assert.Equal(CodecOutputStatus.InputRequired, codec.Receive().Status);
                    }
                    Assert.Equal(48 * 40 * 3 / 2, pixels.AllocatedBytes);
                    using MediaPacket show = Packet(packets[1].Span, input);
                    Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(show));
                }
                else
                {
                    using MediaPacket packet = Packet(encoded, input);
                    Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(packet));
                }
                VideoFrame frame = codec.Receive().Value;
                Assert.Equal(48 * 40 * 3 / 2, pixels.AllocatedBytes);
                codec.Reset();
                int offset = 0;
                for (int plane = 0; plane < frame.Planes.Count; plane++)
                {
                    for (int row = 0; row < frame.Planes[plane].Height; row++)
                    {
                        ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                        Assert.True(expected.AsSpan(offset, actual.Length).SequenceEqual(actual));
                        offset += actual.Length;
                    }
                }
                Assert.Equal(expected.Length, offset);
                Assert.Equal(48 * 40 * 3 / 2, pixels.AllocatedBytes);
                frame.Dispose();
                Assert.Equal(0, pixels.AllocatedBytes);
                using MediaPacket missing = Packet([0x1a, 1, 0xf8], input);
                Assert.Throws<InvalidDataException>(() => codec.TrySend(missing));
            }
            Assert.Equal(0, pixels.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        [Fact]
        public void DisplayedHiddenKeyframeCannotBeShownTwiceThroughAnotherSlot()
        {
            byte[] encoded = Read("hidden.obu");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), allocator))
            {
                using (MediaPacket packet = Packet(encoded, allocator)) { codec.TrySend(packet); }
                codec.Receive().Value.Dispose();
                using (MediaPacket duplicate = Packet([0x1a, 1, 0x88], allocator))
                {
                    Assert.Throws<InvalidDataException>(() => codec.TrySend(duplicate));
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void VisibleKeyframeIsNotAShowableReference()
        {
            byte[] sequence = Read("sequence.obu");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(sequence), allocator))
            {
                using (MediaPacket first = Packet(Av1SequencePictureTests.AccessUnits(sequence)[0].Span, allocator)) { codec.TrySend(first); }
                codec.Receive().Value.Dispose();
                using MediaPacket invalid = Packet([0x1a, 1, 0xf8], allocator);
                Assert.Throws<InvalidDataException>(() => codec.TrySend(invalid));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PaddingAfterVisibleFrameDoesNotCreateAnotherPicture()
        {
            byte[] encoded = [.. Read("hidden.obu"), 0x7a, 3, 0, 0, 0];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(Stream(encoded), allocator))
            {
                using MediaPacket packet = Packet(encoded, allocator);
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(packet));
                codec.Receive().Value.Dispose();
                Assert.Equal(CodecOutputStatus.InputRequired, codec.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static EncodedStreamInfo Stream(ReadOnlySpan<byte> encoded) => new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Av1CodecTests.Configuration(encoded, true));

        private static MediaPacket Packet(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(encoded.Length);
            encoded.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(3, buffer, new(7, new(1, 25)), duration: new(1, new(1, 25)));
        }

        private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));
    }
}
