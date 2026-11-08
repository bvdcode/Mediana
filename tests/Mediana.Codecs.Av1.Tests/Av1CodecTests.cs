// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CodecTests
    {
        [Theory]
        [InlineData("sequence", false)]
        [InlineData("sequence", true)]
        [InlineData("interRepeat", true)]
        [InlineData("interMove", true)]
        [InlineData("interResidual", false)]
        [InlineData("interPrimary", true)]
        [InlineData("interLarge", true)]
        [InlineData("interQuantized10", false)]
        [InlineData("interQuantized30", false)]
        [InlineData("interQuantized50", false)]
        public void PublicCodecPreservesSequencePixelsTimingAndLeases(string fixture, bool configureSequence)
        {
            byte[] encoded = Read(fixture + ".obu");
            byte[] expected = Read(fixture == "sequence" ? "sequence.reference.yuv" : fixture + ".yuv");
            IReadOnlyList<ReadOnlyMemory<byte>> packets = Av1SequencePictureTests.AccessUnits(encoded);
            byte[] configuration = Configuration(packets[0].Span, configureSequence);
            EncodedStreamInfo stream = new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), configuration);
            MediaBufferAllocator allocator = new(1024 * 1024);
            Av1DecoderFactory factory = new();
            Assert.Null(factory.GetUnsupportedReason(stream));
            using (IMediaCodec<MediaPacket, VideoFrame> codec = factory.Create(stream, allocator))
            {
                for (int frameIndex = 0; frameIndex < packets.Count; frameIndex++)
                {
                    ReadOnlyMemory<byte> data = configureSequence ? FrameOnly(packets[frameIndex]) : packets[frameIndex];
                    using (MediaPacket packet = Packet(data.Span, allocator, frameIndex))
                    {
                        Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(packet));
                        Assert.Equal(CodecInputStatus.OutputRequired, codec.TrySend(packet));
                    }
                    using VideoFrame frame = codec.Receive().Value;
                    Assert.Equal(new MediaTimestamp(frameIndex, new(1, 25)), frame.Timestamp);
                    Assert.Equal(new MediaDuration(1, new(1, 25)), frame.Duration);
                    Assert.Same(VideoPixelFormat.Yuv420P8, frame.Format.PixelFormat);
                    Assert.Equal(48, frame.Format.Width);
                    Assert.Equal(40, frame.Format.Height);
                    int source = frameIndex * 48 * 40 * 3 / 2;
                    for (int plane = 0; plane < 3; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                            Assert.True(expected.AsSpan(source, actual.Length).SequenceEqual(actual));
                            source += actual.Length;
                        }
                    }
                    Assert.Equal(CodecOutputStatus.InputRequired, codec.Receive().Status);
                }
                codec.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, codec.Receive().Status);
                codec.Reset();
                using MediaPacket repeat = Packet(configureSequence ? FrameOnly(packets[0]).Span : packets[0].Span, allocator, 0);
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(repeat));
                VideoFrame retained = codec.Receive().Value;
                codec.Dispose();
                Assert.Equal(expected[0], retained.GetRow(0, 0)[0]);
                retained.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailureFaultsUntilResetAndReleasesPendingStorage()
        {
            byte[] encoded = Read("sequence.obu");
            IReadOnlyList<ReadOnlyMemory<byte>> packets = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), Configuration(packets[0].Span, true));
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                using (MediaPacket invalid = Packet([0], allocator, 0))
                {
                    Assert.Throws<NotSupportedException>(() => codec.TrySend(invalid));
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                using MediaPacket valid = Packet(FrameOnly(packets[0]).Span, allocator, 0);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => codec.TrySend(valid, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => codec.CompleteInput());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(valid));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 32)]
        [InlineData(2, 76)]
        [InlineData(3, 1)]
        public void InvalidOrUnsupportedConfigurationFailsBeforeAllocation(int index, byte value)
        {
            byte[] encoded = Read("sequence.obu");
            byte[] configuration = Configuration(encoded, false);
            configuration[index] = value;
            EncodedStreamInfo stream = new(3, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), configuration);
            MediaBufferAllocator allocator = new(1024 * 1024);
            if (index is 1 or 2)
            {
                Assert.Throws<NotSupportedException>(() => new Av1DecoderFactory().Create(stream, allocator));
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => new Av1DecoderFactory().Create(stream, allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static byte[] Configuration(ReadOnlySpan<byte> packet, bool includeSequence)
        {
            Av1ObuReader reader = new(packet);
            int previous = 0;
            while (reader.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1)
                {
                    Av1SequenceHeader sequence = Av1SequenceHeader.Parse(payload);
                    byte[] configuration = new byte[4 + (includeSequence ? reader.ByteOffset - previous : 0)];
                    configuration[0] = 0x81;
                    configuration[1] = (byte)sequence.Tools.Level;
                    configuration[2] = (byte)((sequence.Tools.Tier ? 128 : 0) | (sequence.Monochrome ? 16 : 0) | 12 | sequence.ChromaPosition);
                    if (includeSequence)
                    {
                        packet[previous..reader.ByteOffset].CopyTo(configuration.AsSpan(4));
                    }
                    return configuration;
                }
                previous = reader.ByteOffset;
            }
            throw new InvalidDataException("The fixture has no sequence header.");
        }

        private static ReadOnlyMemory<byte> FrameOnly(ReadOnlyMemory<byte> packet)
        {
            Av1ObuReader reader = new(packet.Span);
            int previous = 0;
            while (reader.TryRead(out int type, out _))
            {
                if (type == 6)
                {
                    return packet[previous..reader.ByteOffset];
                }
                previous = reader.ByteOffset;
            }
            throw new InvalidDataException("The fixture has no frame OBU.");
        }

        private static MediaPacket Packet(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator, int frame)
        {
            using WritableMediaBuffer write = allocator.Allocate(encoded.Length);
            encoded.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(3, buffer, new(frame, new(1, 25)), duration: new(1, new(1, 25)));
        }

        private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));
    }
}
