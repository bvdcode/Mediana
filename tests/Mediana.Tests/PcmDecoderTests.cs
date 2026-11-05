// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

using Mediana.Formats.Audio;

using Mediana.Formats;

namespace Mediana.Tests
{
    public class PcmDecoderTests
    {
        [Theory]
        [InlineData(8, 1)]
        [InlineData(16, 1)]
        [InlineData(24, 1)]
        [InlineData(32, 1)]
        [InlineData(32, 3)]
        [InlineData(64, 3)]
        public void IntegerExtremesAndFloatBitPatternsArePreserved(int bits, int tag)
        {
            ulong[] patterns = (tag, bits) switch
            {
                (1, 8) => [0, 127, 128, 255],
                (1, 16) => [0, 1, 0x7fff, 0x8000, 0xffff],
                (1, 24) => [0, 1, 0x7fffff, 0x800000, 0xffffff],
                (1, 32) => [0, 1, 0x7fffffff, 0x80000000, 0xffffffff],
                (3, 32) => [0, 0x80000000, 0x3f800000, 0xbf000000, 0x7f800000, 0x7fc00001],
                (3, 64) => [0, 0x8000000000000000, 0x3ff0000000000000, 0xbfe0000000000000, 0x7ff0000000000000, 0x7ff8000000000001],
                _ => throw new ArgumentOutOfRangeException(nameof(bits))
            };
            int bytes = bits / 8;
            byte[] input = new byte[patterns.Length * bytes];
            for (int index = 0; index < patterns.Length; index++)
            {
                for (int octet = 0; octet < bytes; octet++)
                {
                    input[index * bytes + octet] = (byte)(patterns[index] >> (octet * 8));
                }
            }
            MediaBufferAllocator allocator = new(2048);
            EncodedStreamInfo stream = Stream(WaveTestInput.Format(bits, 1, tag: tag));
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new PcmDecoderFactory().Create(stream, allocator))
            using (MediaPacket packet = Packet(allocator, input))
            {
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(patterns.Length, frame.SamplesPerChannel);
                Assert.Equal(new MediaTimestamp(123, new TimeBase(1, 48000)), frame.Timestamp);
                Assert.Equal(new MediaDuration(patterns.Length, new TimeBase(1, 48000)), frame.Duration);
                for (int index = 0; index < patterns.Length; index++)
                {
                    ReadOnlySpan<byte> sample = frame.GetPlane(0).Span.Slice(index * frame.Format.BytesPerSample, frame.Format.BytesPerSample);
                    ulong actual = frame.Format.SampleFormat switch
                    {
                        AudioSampleFormat.Unsigned8 => sample[0],
                        AudioSampleFormat.Signed16 => MemoryMarshal.Read<ushort>(sample),
                        AudioSampleFormat.Signed32 or AudioSampleFormat.Float32 => MemoryMarshal.Read<uint>(sample),
                        AudioSampleFormat.Float64 => MemoryMarshal.Read<ulong>(sample),
                        _ => throw new InvalidOperationException("Unexpected PCM sample format.")
                    };
                    Assert.Equal(bits == 24 ? patterns[index] << 8 : patterns[index], actual);
                }
                decoder.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                decoder.Reset();
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ValidPrecisionPaddingAndMissingSpeakerAssignmentsAreExplicit()
        {
            byte[] configuration = WaveTestInput.Format(24, 4, tag: 0xfffe, validBits: 20, mask: 5);
            PcmConfiguration description = PcmConfiguration.ReadWaveFormat(configuration, out string? reason)!;
            Assert.Null(reason);
            Assert.Equal(20, description.ValidBitsPerSample);
            Assert.Equal(new[] { "front-left", "front-center", "discrete-3", "discrete-4" }, description.Format.Channels);
            description = PcmConfiguration.ReadWaveFormat(WaveTestInput.Format(24, 1, tag: 0xfffe, mask: 3), out reason)!;
            Assert.Equal(new[] { "front-left" }, description.Format.Channels);
            MediaBufferAllocator allocator = new(2048);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new PcmDecoderFactory().Create(Stream(configuration), allocator);
            using MediaPacket bad = Packet(allocator, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(bad));
            Assert.Equal(bad.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            using MediaPacket good = Packet(allocator, [0xf0, 0xff, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(good));
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(-4096, MemoryMarshal.Read<int>(frame.GetPlane(0).Span));
        }

        [Fact]
        public void CancellationAllocationFailureAndSampleLimitsReleasePendingStorage()
        {
            MediaBufferAllocator allocator = new(64);
            using MediaPacket packet = Packet(allocator, new byte[6]);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new PcmDecoderFactory().Create(Stream(WaveTestInput.Format(24, 1)), allocator);
            decoder.TrySend(packet);
            Assert.Equal(14, allocator.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => decoder.Receive(new CancellationToken(true)));
            Assert.Equal(6, allocator.AllocatedBytes);
            decoder.Reset();
            decoder.TrySend(packet);
            using AudioFrame frame = decoder.Receive().Value;
            decoder.Dispose();
            Assert.Equal(8, frame.GetPlane(0).Length);
            using IMediaCodec<MediaPacket, AudioFrame> limited = new PcmDecoderFactory(maxSamplesPerFrame: 1).Create(Stream(WaveTestInput.Format(24, 1)), allocator);
            Assert.Throws<NotSupportedException>(() => limited.TrySend(packet));
            Assert.Equal(14, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(12);
            using MediaPacket tooLarge = Packet(small, new byte[6]);
            using IMediaCodec<MediaPacket, AudioFrame> failed = new PcmDecoderFactory().Create(Stream(WaveTestInput.Format(24, 1)), small);
            Assert.Throws<MediaMemoryLimitException>(() => failed.TrySend(tooLarge));
            Assert.Equal(6, small.AllocatedBytes);
        }

        [Theory]
        [InlineData(AudioSampleFormat.Unsigned8)]
        [InlineData(AudioSampleFormat.Signed16)]
        [InlineData(AudioSampleFormat.Signed32)]
        [InlineData(AudioSampleFormat.Float32)]
        [InlineData(AudioSampleFormat.Float64)]
        public void NativeStorageRetainsPayloadAndItsOriginalBudget(AudioSampleFormat sampleFormat) =>
            PcmStorageTestInput.Verify(sampleFormat);

        [Theory]
        [InlineData(16)]
        [InlineData(32)]
        public void ReducedPrecisionStillValidatesUnusedBits(int bits)
        {
            MediaBufferAllocator allocator = new(64);
            using MediaPacket packet = Packet(allocator, [1, 0, 0, 0]);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new PcmDecoderFactory().Create(
                Stream(WaveTestInput.Format(bits, 1, tag: 0xfffe, validBits: bits - 4, mask: 4)), allocator);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
            Assert.Equal(4, allocator.AllocatedBytes);
        }

        [Fact]
        public void MalformedAndUnsupportedConfigurationsAreDistinguished()
        {
            foreach (int offset in new[] { 2, 4, 8, 12, 14 })
            {
                byte[] malformed = WaveTestInput.Format();
                malformed[offset] = 0;
                malformed[offset + 1] = 0;
                Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadWaveFormat(malformed, out _));
            }
            Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadWaveFormat(WaveTestInput.Format(16, tag: 0xfffe, validBits: 17), out _));
            Assert.Null(PcmConfiguration.ReadWaveFormat(WaveTestInput.Format(tag: 6), out string? unsupported));
            Assert.NotNull(unsupported);
            Assert.Null(PcmConfiguration.ReadWaveFormat(WaveTestInput.Format(64), out unsupported));
            Assert.NotNull(unsupported);
            byte[] guid = WaveTestInput.Format(tag: 0xfffe);
            guid[24] = 5;
            Assert.Null(PcmConfiguration.ReadWaveFormat(guid, out unsupported));
            Assert.NotNull(unsupported);
            byte[] extension = WaveTestInput.Format(tag: 0xfffe);
            BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(16), 23);
            Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadWaveFormat(extension, out _));
            Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadWaveFormat(WaveTestInput.Format(32, tag: 3)[..16], out _));
        }

        private static EncodedStreamInfo Stream(byte[] configuration) => new(0, MediaKind.Audio, PcmConfiguration.CodecId, StreamFormats.WavePcm, new TimeBase(1, 48000), configuration);

        private static MediaPacket Packet(MediaBufferAllocator allocator, byte[] bytes)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(123, new TimeBase(1, 48000)));
        }
    }
}
