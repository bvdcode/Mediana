// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class AacEncoderTests
    {
        [Theory]
        [InlineData(48000, 1, 64000)]
        [InlineData(44100, 2, 128000)]
        [InlineData(22050, 2, 96000)]
        [InlineData(8000, 1, 32000)]
        public void ArbitraryPcmBlocksPreserveTimingDelayAndCompleteSignal(int rate, int channels, int bitRate)
        {
            const int Samples = 5107;
            byte[][] first = Encode(rate, channels, bitRate, Samples, [1, 31, 1024, 79, 2049], out List<float> pcm);
            byte[][] second = Encode(rate, channels, bitRate, Samples, [5107], out _);
            Assert.Equal(first.Length, second.Length);
            for (int packet = 0; packet < first.Length; packet++)
            {
                Assert.Equal(first[packet], second[packet]);
            }
            double signal = 0;
            double error = 0;
            for (int sample = 0; sample < Samples; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    double expected = Signal(sample, channel, rate);
                    double actual = pcm[(sample + 1024) * channels + channel];
                    signal += expected * expected;
                    error += (actual - expected) * (actual - expected);
                }
            }
            Assert.True(10 * Math.Log10(signal / error) > 25, $"AAC signal-to-noise ratio: {10 * Math.Log10(signal / error)} dB");
        }

        private static byte[][] Encode(int rate, int channels, int bitRate, int samples, int[] blocks, out List<float> pcm)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = Format(rate, channels);
            using AacEncoder encoder = AacEncoder.Create(format, new(1, rate), allocator, new() { BitRate = bitRate }, 5);
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(encoder.Stream, allocator);
            List<byte[]> packets = [];
            List<float> decoded = [];
            int offset = 0;
            int block = 0;
            while (offset < samples)
            {
                int count = Math.Min(blocks[block++ % blocks.Length], samples - offset);
                using AudioFrame frame = Frame(format, allocator, offset, count);
                Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
                frame.Dispose();
                Drain();
                offset += count;
            }
            encoder.CompleteInput();
            Drain();
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
            Assert.Equal(samples, encoder.InputSamplesPerChannel);
            Assert.Equal(1024, encoder.PrimingSamples);
            Assert.Equal((1024 - samples % 1024) % 1024, encoder.PaddingSamples);
            Assert.Equal((samples + 1023) / 1024 + 1, packets.Count);
            decoder.Dispose();
            encoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            pcm = decoded;
            return packets.ToArray();

            void Drain()
            {
                while (true)
                {
                    CodecOutput<MediaPacket> output = encoder.Receive();
                    if (output.Status != CodecOutputStatus.Available)
                    {
                        return;
                    }
                    using MediaPacket packet = output.Value;
                    Assert.Equal(new MediaTimestamp((packets.Count - 1) * 1024, new(1, rate)), packet.PresentationTime);
                    Assert.Equal(packet.PresentationTime, packet.DecodeTime);
                    Assert.Equal(new MediaDuration(1024, new(1, rate)), packet.Duration);
                    Assert.Equal(5, packet.StreamIndex);
                    Assert.InRange(packet.Data.Length * 8, 1, (long)bitRate * 1024 / rate);
                    packets.Add(packet.Data.ToArray());
                    decoder.TrySend(packet);
                    using AudioFrame frame = decoder.Receive().Value;
                    decoded.AddRange(MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span).ToArray());
                }
            }
        }

        [Fact]
        public void CancelledEncoderRequiresResetAndReleasesRetainedInput()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = Format(48000, 1);
            using AacEncoder encoder = AacEncoder.Create(format, new(1, 48000), allocator);
            using AudioFrame frame = Frame(format, allocator, 0, 123);
            encoder.TrySend(frame);
            frame.Dispose();
            Assert.ThrowsAny<OperationCanceledException>(() => encoder.Receive(new(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => encoder.Receive());
            encoder.Reset();
            Assert.Equal(CodecOutputStatus.InputRequired, encoder.Receive().Status);
            encoder.CompleteInput();
            Assert.Equal(CodecOutputStatus.EndOfStream, encoder.Receive().Status);
        }

        [Fact]
        public void DiscontinuousInputFailsWithoutSilentlyInsertingSilence()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = Format(48000, 1);
            using AacEncoder encoder = AacEncoder.Create(format, new(1, 48000), allocator);
            using AudioFrame first = Frame(format, allocator, 0, 17);
            encoder.TrySend(first);
            Assert.Equal(CodecOutputStatus.InputRequired, encoder.Receive().Status);
            using AudioFrame next = Frame(format, allocator, 18, 17);
            Assert.Throws<InvalidDataException>(() => encoder.TrySend(next));
            encoder.Reset();
            Assert.Null(encoder.PaddingSamples);
            Assert.Equal(0, encoder.InputSamplesPerChannel);
        }

        [Fact]
        public void ReturnedPacketSurvivesResetAndEncoderDisposal()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = Format(48000, 2);
            AacEncoder encoder = AacEncoder.Create(format, new(1, 48000), allocator);
            using AudioFrame frame = Frame(format, allocator, 0, 1024);
            encoder.TrySend(frame);
            using MediaPacket packet = encoder.Receive().Value;
            byte[] expected = packet.Data.ToArray();
            encoder.Reset();
            encoder.Dispose();
            Assert.Equal(expected, packet.Data.ToArray());
            packet.Dispose();
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BackpressureLeavesTheNextBorrowedInputUntouched()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = Format(48000, 1);
            using AacEncoder encoder = AacEncoder.Create(format, new(1, 48000), allocator);
            using AudioFrame first = Frame(format, allocator, 0, 2048);
            using AudioFrame next = Frame(format, allocator, 2048, 17);
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(first));
            Assert.Equal(CodecInputStatus.OutputRequired, encoder.TrySend(next));
            using MediaPacket packet = encoder.Receive().Value;
            Assert.Equal(CodecInputStatus.OutputRequired, encoder.TrySend(next));
            using MediaPacket second = encoder.Receive().Value;
            Assert.Equal(CodecOutputStatus.InputRequired, encoder.Receive().Status);
            Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(next));
        }

        [Theory]
        [InlineData(48000, 1)]
        [InlineData(48000, 1000000)]
        public void UnsupportedBudgetsAreRejected(int rate, int bitRate)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.Create(Format(rate, 1), new(1, rate), allocator, new() { BitRate = bitRate }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnsupportedRateAndInexactSampleClockAreRejected()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<NotSupportedException>(() => AacEncoder.Create(Format(7350, 1), new(1, 7350), allocator));
            Assert.Throws<NotSupportedException>(() => AacEncoder.Create(Format(48000, 1), new(1, 1000), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static AudioFormat Format(int rate, int channels) => new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
            channels == 1 ? ["front-center"] : ["front-left", "front-right"]);

        internal static AudioFrame Frame(AudioFormat format, MediaBufferAllocator allocator, int start, int count)
        {
            using WritableMediaBuffer storage = allocator.Allocate(format.GetBufferSize(count));
            Span<float> pcm = MemoryMarshal.Cast<byte, float>(storage.Memory.Span);
            for (int sample = 0; sample < count; sample++)
            {
                for (int channel = 0; channel < format.Channels.Count; channel++)
                {
                    pcm[sample * format.Channels.Count + channel] = (float)Signal(start + sample, channel, format.SampleRate);
                }
            }
            using MediaBuffer buffer = storage.Publish();
            return new(format, count, buffer, new(start, new(1, format.SampleRate)));
        }

        private static double Signal(int sample, int channel, int rate) =>
            0.4 * Math.Sin(2 * Math.PI * (channel == 0 ? 997 : 1567) * sample / rate) + 0.15 * Math.Cos(2 * Math.PI * 319 * sample / rate);
    }
}
