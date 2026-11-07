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
    public class AacIntervalEncodingTests
    {
        [Theory]
        [InlineData(48000, 1, 64000)]
        [InlineData(44100, 2, 128000)]
        [InlineData(22050, 1, 48000)]
        [InlineData(8000, 2, 64000)]
        public void IndependentlyRequestedWindowsMatchEveryContinuousPacketIncludingTransientChangesAndTail(int rate, int channels, int bitRate)
        {
            const int Samples = 64 * 1024 + 333;
            TimeBase clock = new(1, 2 * rate);
            MediaTimestamp origin = new(-1001, clock);
            MediaTimeRange source = new(origin, new(origin.Value + 2 * Samples, clock));
            AudioFormat format = new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
                channels == 1 ? ["front-center"] : ["front-left", "front-right"]);
            AacEncoderOptions options = new() { BitRate = bitRate };
            ProcessingPacket[] continuous = Encode(format, source, source, options, false);
            long[] cuts = [0, 1, 27, 2 * 3 * 1024 + 17, 2 * 7 * 1024, 2 * 15 * 1024 + 1,
                2 * 25 * 1024 + 33, 2 * 48 * 1024 - 1, 2 * 63 * 1024 + 7, 2 * Samples];
            List<ProcessingPacket> assembled = [];
            for (int index = 0; index < cuts.Length - 1; index++)
            {
                MediaTimeRange requested = new(new(origin.Value + cuts[index], clock), new(origin.Value + cuts[index + 1], clock));
                ProcessingPacket[] packets = Encode(format, requested, source, options, true);
                assembled.AddRange(packets);
                foreach (ProcessingPacket packet in packets)
                {
                    ProcessingPacket expected = Assert.Single(continuous, candidate => candidate.Pts == packet.Pts);
                    Assert.Equal(expected.Data, packet.Data);
                    Assert.Equal(expected.Duration, packet.Duration);
                }
            }
            Assert.Equal(continuous.Select(packet => packet.Pts), assembled.Select(packet => packet.Pts));
            Assert.Equal((Samples + 1023) / 1024 + 1, continuous.Length);
            foreach (int index in new[] { 7, 3, 8, 1, 7 })
            {
                MediaTimeRange requested = new(new(origin.Value + cuts[index], clock), new(origin.Value + cuts[index + 1], clock));
                ProcessingPacket[] packets = Encode(format, requested, source, options, true);
                foreach (ProcessingPacket packet in packets)
                { Assert.Equal(Assert.Single(continuous, candidate => candidate.Pts == packet.Pts).Data, packet.Data); }
            }
        }

        [Fact]
        public void ResetRestoresTheGlobalFractionalBitBudget()
        {
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            AudioFormat format = AacEncoderTests.Format(44100, 2);
            using AacEncoder encoder = AacEncoder.CreateAtFrameOffset(format, new(1, 44100), allocator, 123456789,
                new() { BitRate = 127999 });
            using AudioFrame frame = AacEncoderTests.Frame(format, allocator, 0, 2048);
            encoder.TrySend(frame);
            using MediaPacket first = encoder.Receive().Value;
            byte[] expected = first.Data.ToArray();
            encoder.Reset();
            encoder.TrySend(frame);
            using MediaPacket replay = encoder.Receive().Value;
            Assert.Equal(expected, replay.Data.ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.CreateAtFrameOffset(format, new(1, 44100), allocator, -1));
        }

        [Fact]
        public void PlanningUsesExactSampleGridAndConstantWarmupWithoutDependingOnThePrecedingDuration()
        {
            MediaTimeRange source = new(new(7, new(1, 2)), new(360007, new(1, 2)));
            MediaTimeRange requested = new(new(100000, new(1, 1)), new(100001, new(1, 1)));
            AacEncodingInterval plan = AacEncodingInterval.Create(requested, source, 48000);
            Assert.True(plan.HasPackets);
            Assert.InRange(plan.Input.Duration.Value, 48000, 48000 + 7 * 1024);
            Assert.Equal(new TimeBase(1, 48000), plan.Input.Start.TimeBase);
            Assert.InRange(requested.Start.Rescale(new(1, 48000), TimestampRounding.TowardZero).Value - plan.Input.Start.Value, 3073, 4096);
            Assert.Equal(0, (plan.Input.Start.Value - source.Start.Rescale(new(1, 48000), TimestampRounding.TowardZero).Value) % 1024);
            Assert.False(AacEncodingInterval.Create(new(source.End, new(source.End.Value + 1, source.End.TimeBase)), source, 48000).HasPackets);
            Assert.False(AacEncodingInterval.Create(new(requested.Start, requested.Start), source, 48000).HasPackets);
            Assert.Throws<ArgumentException>(() => AacEncodingInterval.Create(requested, new(new(0, new(1, 1)), new(1, new(1, 7))), 48000));
        }

        private static ProcessingPacket[] Encode(AudioFormat format, MediaTimeRange requested, MediaTimeRange source,
            AacEncoderOptions options, bool bounded)
        {
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            AacEncodingInterval plan = AacEncodingInterval.Create(requested, source, format.SampleRate);
            if (!plan.HasPackets) { return []; }
            MediaTimeRange input = bounded ? plan.Input : source;
            TimeBase clock = input.Start.TimeBase;
            MediaTimestamp sourceOrigin = source.Start.Rescale(clock, TimestampRounding.TowardZero);
            long sampleTicks = clock.Denominator / format.SampleRate;
            int first = checked((int)((input.Start.Value - sourceOrigin.Value) / sampleTicks));
            int end = checked((int)((input.End.Value - sourceOrigin.Value) / sampleTicks));
            IAudioEncoderFactory factory = bounded ? new AacIntervalEncoderFactory(plan, options) : new AacEncoderFactory(options);
            using IMediaEncoder<AudioFrame> encoder = factory.Create(format, clock, allocator, 3);
            List<ProcessingPacket> packets = [];
            for (int position = first; position < end;)
            {
                int count = Math.Min(4093, end - position);
                using WritableMediaBuffer writable = allocator.Allocate(count * format.Channels.Count * sizeof(float));
                Span<float> pcm = MemoryMarshal.Cast<byte, float>(writable.Memory.Span);
                for (int sample = 0; sample < count; sample++)
                {
                    int absolute = position + sample;
                    for (int channel = 0; channel < format.Channels.Count; channel++)
                    {
                        double envelope = (absolute / 1024 % 9) switch { 0 or 1 => 0, 2 => 0.9, 3 or 4 => 0.03, _ => 0.5 };
                        uint noise = unchecked((uint)(absolute * 1103515245 + channel * 12345));
                        pcm[sample * format.Channels.Count + channel] = (float)(envelope *
                            (0.7 * Math.Sin(absolute * (0.13 + channel * 0.07)) + 0.3 * ((noise & 65535) / 32768d - 1)));
                    }
                }
                using MediaBuffer storage = writable.Publish();
                using AudioFrame frame = new(format, count, storage,
                    new(sourceOrigin.Value + position * sampleTicks, clock));
                Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(frame));
                Drain();
                position += count;
            }
            encoder.CompleteInput();
            Drain();
            encoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            return packets.ToArray();

            void Drain()
            {
                while (true)
                {
                    CodecOutput<MediaPacket> output = encoder.Receive();
                    if (output.Status != CodecOutputStatus.Available) { break; }
                    using MediaPacket packet = output.Value;
                    if (!bounded || (packet.PresentationTime!.Value.CompareTo(plan.Output.Start) >= 0 && packet.PresentationTime.Value.CompareTo(plan.Output.End) < 0))
                    { packets.Add(new(packet.StreamIndex, packet.PresentationTime, packet.DecodeTime, packet.Duration, packet.Data.ToArray())); }
                }
            }
        }
    }
}
