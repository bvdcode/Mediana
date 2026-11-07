// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.IO;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusDecoderTests
    {
        [Fact]
        public void DtxBeforeReceivedSpeechPublishesTimestampedSilence()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator);
            using WritableMediaBuffer writable = allocator.Allocate(1); writable.Span[0] = 248;
            using MediaBuffer buffer = writable.Publish();
            using MediaPacket packet = new(0, buffer, new(1920, new(1, 48000)));
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
            using AudioFrame frame = decoder.Receive().Value;
            Assert.Equal(960, frame.SamplesPerChannel); Assert.Equal(packet.PresentationTime, frame.Timestamp);
            Assert.True(frame.GetPlane(0).Span.IndexOfAnyExcept((byte)0) < 0);
        }

        [Fact]
        public void MultistreamMappingAndPacketFramePackingMatchIndependentPcm() =>
            Assert.Equal(144, OpusMultistreamTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public async Task WholeOggDecodingAppliesPreSkipAndEndGranuleWithoutChangingTheSampleGrid()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "opus-stereo.ogg"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "ogg-stereo.f32le"));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(encoded));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new OggReaderFactory()).AddAudioDecoder(new OpusDecoderFactory()).Build();
            await using AudioSession session = await engine.OpenAudioAsync(source);
            int written = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(written / 8, new(1, 48000)), frame.Timestamp);
                    Compare(frame.GetPlane(0).Span, expected.AsSpan(written, frame.GetPlane(0).Length), written / 8);
                    written += frame.GetPlane(0).Length;
                }
            }
            Assert.Equal(expected.Length, written);
            await session.DisposeAsync(); Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task IndexedRestartsMatchIndependentDecoderWithTheSamePreroll()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "opus-stereo.ogg"));
            using BinaryReader reference = new(File.OpenRead(Path.Combine(root, "ogg-seek.bin")));
            Assert.True(reference.ReadBytes(8).AsSpan().SequenceEqual("OPUSSEK1"u8));
            Assert.Equal(5, reference.ReadInt32());
            await using RandomAccessStreamByteSource source = new(new MemoryStream(encoded));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new OggReaderFactory()).AddAudioDecoder(new OpusDecoderFactory()).Build();
            await using AudioSession session = await engine.OpenAudioAsync(source);
            foreach (int position in new[] { 60000, 24000, 0, 60000, 0 })
            {
                int packet = reference.ReadInt32(), first = reference.ReadInt32();
                int expectedStart = reference.ReadInt32(), samples = reference.ReadInt32();
                Assert.InRange(packet - first, 0, 4);
                byte[] expected = reference.ReadBytes(samples * 8);
                await session.SeekAsync(new(position, new(1, 48000)));
                using AudioFrame frame = (await session.ReadFrameAsync())!;
                Assert.Equal(new MediaTimestamp(expectedStart, new(1, 48000)), frame.Timestamp);
                Assert.Equal(samples, frame.SamplesPerChannel);
                Compare(frame.GetPlane(0).Span, expected, expectedStart);
            }
            Assert.Equal(reference.BaseStream.Length, reference.BaseStream.Position);
            await session.DisposeAsync(); Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public void PrimingSpansPacketsAndPrerollDoesNotApplyItAgainMidstream()
        {
            MediaBufferAllocator allocator = new(512 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator, 1000);
            using MediaPacket packet = Packet(allocator, 7000);
            decoder.TrySend(packet); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            decoder.TrySend(packet);
            using AudioFrame trimmed = decoder.Receive().Value;
            Assert.Equal(920, trimmed.SamplesPerChannel);
            Assert.Equal(new MediaTimestamp(7040, new(1, 48000)), trimmed.Timestamp);
            IPacketPrerollDecoder preroll = (IPacketPrerollDecoder)decoder;
            preroll.ResetForPreroll(1, false);
            decoder.TrySend(packet); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
            decoder.TrySend(packet);
            using AudioFrame untrimmed = decoder.Receive().Value;
            Assert.Equal(960, untrimmed.SamplesPerChannel); Assert.Equal(packet.PresentationTime, untrimmed.Timestamp);
            decoder.CompleteInput(); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            byte[] retained = trimmed.GetPlane(0).ToArray();
            decoder.Dispose(); Assert.Equal(retained, trimmed.GetPlane(0).ToArray());
        }

        [Theory]
        [InlineData(-32768)]
        [InlineData(-1536)]
        [InlineData(1536)]
        [InlineData(32767)]
        public void HeaderGainScalesFloatOutputWithoutClipping(short gain)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "decoded-hybrid.bin")));
            reader.ReadBytes(24); byte[] encoded = reader.ReadBytes(reader.ReadInt32());
            MediaBufferAllocator allocator = new(512 * 1024);
            using WritableMediaBuffer writable = allocator.Allocate(encoded.Length); encoded.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish(); using MediaPacket packet = new(0, buffer);
            using IMediaCodec<MediaPacket, AudioFrame> original = Create(allocator);
            using IMediaCodec<MediaPacket, AudioFrame> adjusted = Create(allocator, gain: gain);
            original.TrySend(packet); adjusted.TrySend(packet);
            using AudioFrame normal = original.Receive().Value, scaled = adjusted.Receive().Value;
            ReadOnlySpan<float> before = MemoryMarshal.Cast<byte, float>(normal.GetPlane(0).Span);
            ReadOnlySpan<float> after = MemoryMarshal.Cast<byte, float>(scaled.GetPlane(0).Span);
            float scale = (float)Math.Pow(10, gain / (20.0 * 256));
            for (int at = 0; at < before.Length; at++) { Assert.Equal(before[at] * scale, after[at]); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(360)]
        [InlineData(960)]
        public void EndTrimmingPublishesOnlyTheDeclaredSamples(int duration)
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator);
            using MediaPacket packet = Packet(allocator, 0, duration);
            decoder.TrySend(packet);
            if (duration == 0) { Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status); }
            else
            {
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(duration, frame.SamplesPerChannel); Assert.Equal(duration * sizeof(float), frame.GetPlane(0).Length);
            }
            decoder.CompleteInput(); Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
        }

        [Fact]
        public void BackpressureRetainedOutputAndFailedInputRespectOwnership()
        {
            MediaBufferAllocator allocator = new(256 * 1024);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator);
            using MediaPacket packet = Packet(allocator, 0);
            decoder.TrySend(packet); Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
            using AudioFrame frame = decoder.Receive().Value;
            using MediaPacket invalid = Packet(allocator, 0, 961);
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(invalid));
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            Assert.Equal(960 * sizeof(float), frame.GetPlane(0).Length);
            decoder.Reset(); decoder.TrySend(packet);
            using AudioFrame recovered = decoder.Receive().Value;
            decoder.Dispose(); Assert.True(frame.GetPlane(0).Span.SequenceEqual(recovered.GetPlane(0).Span));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(14000)]
        [InlineData(105000)]
        [InlineData(130000)]
        public void BudgetFailureReleasesPartiallyPreparedTablesAndChannels(long limit)
        {
            MediaBufferAllocator allocator = new(limit);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator);
            using MediaPacket packet = Packet(allocator, 0);
            Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
        }

        private static IMediaCodec<MediaPacket, AudioFrame> Create(MediaBufferAllocator allocator, ushort preSkip = 0, short gain = 0)
        {
            byte[] header = new byte[19]; "OpusHead"u8.CopyTo(header); header[8] = 1; header[9] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), preSkip);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(16), gain);
            return new OpusDecoderFactory().Create(new(0, MediaKind.Audio, "opus", StreamFormats.Opus, new(1, 48000), header), allocator);
        }

        private static MediaPacket Packet(MediaBufferAllocator allocator, long timestamp, int duration = 960)
        {
            using WritableMediaBuffer writable = allocator.Allocate(3);
            new byte[] { 248, 255, 254 }.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer, new(timestamp, new(1, 48000)), duration: new(duration, new(1, 48000)));
        }

        private static void Compare(ReadOnlySpan<byte> actualBytes, ReadOnlySpan<byte> expectedBytes, int offset)
        {
            ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(actualBytes), expected = MemoryMarshal.Cast<byte, float>(expectedBytes);
            double signal = 0, error = 0;
            for (int at = 0; at < actual.Length; at++)
            {
                double difference = (double)actual[at] - expected[at];
                signal += (double)expected[at] * expected[at]; error += difference * difference;
                Assert.True(float.IsFinite(actual[at]));
            }
            double relative = Math.Sqrt(error / Math.Max(signal, 1e-20));
            Assert.True(relative < .00002, $"PCM at sample {offset}: relative error {relative}; first {actual[0]} / {expected[0]}.");
        }
    }
}
