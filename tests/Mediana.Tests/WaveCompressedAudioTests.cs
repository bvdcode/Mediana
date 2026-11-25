// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Wave;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class WaveCompressedAudioTests
    {
        private const int Rate = 8000;
        private static readonly TimeBase sampleClock = new(1, Rate);

        [Theory]
        [InlineData(WaveCompressedAudioEncoding.ALaw, 3)]
        [InlineData(WaveCompressedAudioEncoding.ImaAdpcm, 12)]
        public async Task ProbeAndWaveformUseTheSameExactSampleCount(WaveCompressedAudioEncoding encoding, int samples)
        {
            byte[] file = encoding == WaveCompressedAudioEncoding.ALaw ? ALaw() : ImaFourBit();
            WaveCompressedAudioCodec codec = new(encoding);
            MediaEngineBuilder builder = new MediaEngineBuilder()
                .AddContainer(new WaveReaderFactory(new WaveReadOptions { SamplesPerPacket = 2 }))
                .AddStreamMetadataReader(codec);
            await using (RandomAccessStreamByteSource source = Open(file))
            {
                MediaInfo info = await builder.Build().ProbeAsync(source);
                EncodedStreamInfo stream = Assert.Single(info.Streams);
                Assert.Equal(codec.CodecId, stream.CodecId);
                Assert.Equal(new MediaDuration(samples, sampleClock), stream.CodedDuration);
                AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
                Assert.Equal(Rate, metadata.SampleRate);
                Assert.Equal(2, metadata.ChannelCount);
                Assert.Equal(["front-left", "front-right"], metadata.ChannelOrder);
                Assert.Null(metadata.BitsPerSample);
            }

            MediaEngine engine = builder.AddAudioDecoder(codec).Build();
            await using RandomAccessStreamByteSource audio = Open(file);
            await using AudioSession session = await engine.OpenAudioAsync(audio, 0);
            MediaBufferAllocator allocator = new(65536);
            await using (AudioWaveformReader waveform = new(session.ReadFrameAsync, [1, 0], new AudioWaveformOptions(Rate, 2), allocator))
            {
                int count = 0;
                while (await waveform.ReadLevelAsync() is AudioLevel level)
                {
                    Assert.Equal(new MediaTimestamp(count, sampleClock), level.Timestamp);
                    Assert.True(level.Peak > 0);
                    count += level.SampleCount;
                }
                Assert.Equal(samples, count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ALawExpandsInterleavedSamples()
        {
            await using RandomAccessStreamByteSource source = Open(ALaw());
            await using AudioSession session = await AudioEngine(WaveCompressedAudioEncoding.ALaw).OpenAudioAsync(source, 0);
            using AudioFrame frame = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
            Assert.Equal(AudioSampleFormat.Signed16, frame.Format.SampleFormat);
            Assert.Equal(new short[] { 8, -8, 5504, -5504, 32256, -32256 }, Samples(frame));
            Assert.Null(await session.ReadFrameAsync());
        }

        [Fact]
        public async Task ImaFourBitResetsPerBlockAndLimitsTheFinalFrame()
        {
            await using RandomAccessStreamByteSource source = Open(ImaFourBit());
            await using AudioSession session = await AudioEngine(WaveCompressedAudioEncoding.ImaAdpcm, 9).OpenAudioAsync(source, 0);
            using AudioFrame first = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
            using AudioFrame second = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
            Assert.Equal(new MediaTimestamp(0, sampleClock), first.Timestamp);
            Assert.Equal(new MediaTimestamp(9, sampleClock), second.Timestamp);
            Assert.Equal(new short[] { 1000, -1000, 1000, -999, 1000, -998, 1000, -997, 1000, -996,
                1000, -995, 1000, -994, 1000, -993, 1000, -992 }, Samples(first));
            Assert.Equal(new short[] { -300, 300, -289, 289, -259, 259 }, Samples(second));
            Assert.Null(await session.ReadFrameAsync());
        }

        [Fact]
        public async Task ImaThreeBitReadsCodesAcrossInterleavedWordBoundaries()
        {
            byte[] block = [100, 0, 0, 0, 12, 254, 0, 0,
                0x49, 0x92, 0x24, 0x49, 0x6d, 0xdb, 0xb6, 0x6d,
                0x92, 0x24, 0x49, 0x92, 0xdb, 0xb6, 0x6d, 0xdb,
                0x24, 0x49, 0x92, 0x24, 0xb6, 0x6d, 0xdb, 0xb6];
            byte[] file = WaveTestInput.File(("fmt ", ImaFormat(3, 56, 65)), ("fact", Fact(33)), ("data", block));
            await using RandomAccessStreamByteSource source = Open(file);
            await using AudioSession session = await AudioEngine(WaveCompressedAudioEncoding.ImaAdpcm).OpenAudioAsync(source, 0);
            using AudioFrame frame = Assert.IsType<AudioFrame>(await session.ReadFrameAsync());
            short[] actual = Samples(frame);
            Assert.Equal(66, actual.Length);
            for (int sample = 0; sample < 33; sample++)
            {
                Assert.Equal((short)(100 + sample * 4), actual[sample * 2]);
                Assert.Equal((short)(-500 - sample * 4), actual[sample * 2 + 1]);
            }
            Assert.Null(await session.ReadFrameAsync());
        }

        [Theory]
        [InlineData(16, 10)]
        [InlineData(16, 0)]
        [InlineData(15, 8)]
        public async Task InvalidImaDataGeometryAndFactFailAtOpen(int dataBytes, int samples)
        {
            byte[] file = WaveTestInput.File(("fmt ", ImaFormat(4, 16, 9)), ("fact", Fact(samples)), ("data", new byte[dataBytes]));
            await using RandomAccessStreamByteSource source = Open(file);
            MediaBufferAllocator allocator = new(65536);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await WaveReader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] ALaw() => WaveTestInput.File(("fmt ", WaveTestInput.Format(8, 2, Rate, 6)),
            ("fact", Fact(3)), ("data", [0xd5, 0x55, 0x80, 0x00, 0xaa, 0x2a]));

        private static byte[] ImaFourBit() => WaveTestInput.File(("fmt ", ImaFormat(4, 16, 9)),
            ("fact", Fact(12)), ("data", [232, 3, 0, 0, 24, 252, 0, 0, 0, 0, 0, 0, 0x11, 0x11, 0x11, 0x11,
                212, 254, 0, 0, 44, 1, 0, 0, 0x77, 0, 0, 0, 0xff, 0, 0, 0]));

        private static byte[] ImaFormat(int bits, ushort alignment, ushort samples)
        {
            byte[] format = [.. WaveTestInput.Format(bits, 2, Rate, 0x11), 0, 0];
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), alignment);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(16), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(18), samples);
            return format;
        }

        private static byte[] Fact(int samples)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, samples);
            return bytes;
        }

        private static RandomAccessStreamByteSource Open(byte[] file) => new(new MemoryStream(file));

        private static MediaEngine AudioEngine(WaveCompressedAudioEncoding encoding, int samplesPerPacket = 4096) =>
            new MediaEngineBuilder().AddContainer(new WaveReaderFactory(new WaveReadOptions { SamplesPerPacket = samplesPerPacket }))
                .AddAudioDecoder(new WaveCompressedAudioCodec(encoding)).Build();

        private static short[] Samples(AudioFrame frame) => MemoryMarshal.Cast<byte, short>(frame.GetPlane(0).Span).ToArray();
    }
}
