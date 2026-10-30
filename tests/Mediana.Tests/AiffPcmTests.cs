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

namespace Mediana.Tests
{
    public class AiffPcmTests
    {
        [Theory]
        [InlineData(1, AudioSampleFormat.Unsigned8, 8)]
        [InlineData(8, AudioSampleFormat.Unsigned8, 8)]
        [InlineData(12, AudioSampleFormat.Signed16, 16)]
        [InlineData(16, AudioSampleFormat.Signed16, 16)]
        [InlineData(20, AudioSampleFormat.Signed32, 24)]
        [InlineData(24, AudioSampleFormat.Signed32, 24)]
        [InlineData(32, AudioSampleFormat.Signed32, 32)]
        public void CommonFieldsPreserveSourcePrecisionAndByteOrder(int bits, AudioSampleFormat expected, int storage)
        {
            PcmConfiguration parsed = PcmConfiguration.ReadAiffFormat(AiffTestInput.Configuration(bits), out string? reason)!;
            Assert.Null(reason);
            Assert.Equal(48000, parsed.Format.SampleRate);
            Assert.Equal(new[] { "front-left", "front-right" }, parsed.Format.Channels);
            Assert.Equal(expected, parsed.Format.SampleFormat);
            Assert.Equal(bits, parsed.ValidBitsPerSample);
            Assert.Equal(storage, parsed.SourceBitsPerSample);
            Assert.True(parsed.IsBigEndian);
        }

        [Theory]
        [InlineData(8, null, new byte[] { 0x80, 0xff, 0, 0x7f }, new long[] { 0, 127, 128, 255 })]
        [InlineData(12, null, new byte[] { 0x80, 0, 0xff, 0xf0, 0, 0, 0x7f, 0xf0 }, new long[] { -32768, -16, 0, 32752 })]
        [InlineData(16, "NONE", new byte[] { 0x80, 0, 0xff, 0xff, 0, 0, 0x7f, 0xff }, new long[] { -32768, -1, 0, 32767 })]
        [InlineData(16, "sowt", new byte[] { 0, 0x80, 0xff, 0xff, 0, 0, 0xff, 0x7f }, new long[] { -32768, -1, 0, 32767 })]
        [InlineData(24, null, new byte[] { 0x80, 0, 0, 0xff, 0xff, 0xff, 0, 0, 0, 0x7f, 0xff, 0xff }, new long[] { int.MinValue, -256, 0, 2147483392 })]
        [InlineData(32, null, new byte[] { 0x80, 0, 0, 0, 0xff, 0xff, 0xff, 0xff, 0, 0, 0, 0, 0x7f, 0xff, 0xff, 0xff }, new long[] { int.MinValue, -1, 0, int.MaxValue })]
        public void IntegerSamplesDecodeExactly(int bits, string? compression, byte[] input, long[] expected)
        {
            MediaBufferAllocator allocator = new(4096);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(bits, compression, allocator))
            {
                using MediaPacket packet = Packet(input, allocator);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using AudioFrame frame = decoder.Receive().Value;
                ReadOnlySpan<byte> output = frame.GetPlane(0).Span;
                for (int index = 0; index < expected.Length; index++)
                {
                    long actual = bits switch
                    {
                        8 => output[index],
                        12 or 16 => MemoryMarshal.Read<short>(output[(index * 2)..]),
                        24 or 32 => MemoryMarshal.Read<int>(output[(index * 4)..]),
                        _ => throw new ArgumentOutOfRangeException(nameof(bits))
                    };
                    Assert.Equal(expected[index], actual);
                }
                decoder.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(32, "fl32")]
        [InlineData(64, "fl64")]
        public void FloatingPointBitPatternsSurviveBigEndianDecoding(int bits, string compression)
        {
            byte[] bytes = new byte[bits / 8 * 4];
            for (int index = 0; index < 4; index++)
            {
                if (bits == 32)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index * 4), new uint[] { 0x80000000, 0x3f000000, 0xff800000, 0x7fc01234 }[index]);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(index * 8), new ulong[] { 0x8000000000000000, 0x3fe0000000000000, 0xfff0000000000000, 0x7ff8000000001234 }[index]);
                }
            }
            MediaBufferAllocator allocator = new(4096);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(bits, compression, allocator))
            {
                using MediaPacket packet = Packet(bytes, allocator);
                decoder.TrySend(packet);
                using AudioFrame frame = decoder.Receive().Value;
                if (BitConverter.IsLittleEndian)
                {
                    for (int offset = 0; offset < bytes.Length; offset += bits / 8)
                    {
                        Array.Reverse(bytes, offset, bits / 8);
                    }
                }
                Assert.Equal(bytes, frame.GetPlane(0).ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FractionalAndUnsupportedRatesAreExplicitAndMalformedRatesThrow()
        {
            byte[] fractional = AiffTestInput.Configuration();
            fractional[21] = 1;
            Assert.Null(PcmConfiguration.ReadAiffFormat(fractional, out string? reason));
            Assert.Contains("integral", reason);
            foreach (byte[] rate in new byte[][]
            {
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                [0xc0, 0x0e, 0xbb, 0x80, 0, 0, 0, 0, 0, 0],
                [0x7f, 0xff, 0x80, 0, 0, 0, 0, 0, 0, 0],
                [0x40, 0x0e, 0x3b, 0x80, 0, 0, 0, 0, 0, 0]
            })
            {
                byte[] configuration = AiffTestInput.Configuration();
                rate.CopyTo(configuration, 12);
                Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadAiffFormat(configuration, out _));
            }
        }

        [Fact]
        public void UnsupportedCompressionAndMalformedNamesDoNotGetInterpretedAsPcm()
        {
            Assert.Null(PcmConfiguration.ReadAiffFormat(AiffTestInput.Configuration(16, "ima4"), out string? reason));
            Assert.NotNull(reason);
            byte[] bytes = AiffTestInput.Configuration(16, "NONE");
            bytes[26] = 4;
            Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadAiffFormat(bytes, out _));
            Assert.Throws<InvalidDataException>(() => PcmConfiguration.ReadAiffFormat(AiffTestInput.Configuration(16, "fl32"), out _));
        }

        [Fact]
        public void InvalidPaddingFaultsTheDecoderWithoutRetainingStorage()
        {
            MediaBufferAllocator allocator = new(4096);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(12, null, allocator))
            {
                using MediaPacket packet = Packet([0, 1, 0, 0], allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static IMediaCodec<MediaPacket, AudioFrame> Create(int bits, string? compression, MediaBufferAllocator allocator) =>
            new AiffPcmDecoderFactory().Create(new EncodedStreamInfo(0, MediaKind.Audio, PcmConfiguration.AiffCodecId,
                new TimeBase(1, 48000), AiffTestInput.Configuration(bits, compression)), allocator);

        private static MediaPacket Packet(byte[] bytes, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length);
            bytes.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new MediaPacket(0, buffer, new MediaTimestamp(0, new TimeBase(1, 48000)));
        }
    }
}
