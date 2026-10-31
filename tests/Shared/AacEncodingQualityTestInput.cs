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
    internal static class AacEncodingQualityTestInput
    {
        public static void Verify(int activeChannel)
        {
            const int Rate = 48000;
            const int Samples = 108037;
            const int BitRate = 96000;
            float[] source = new float[Samples * 2];
            int[] frequencies = [157, 311, 617, 1223, 2441, 4871, 9743];
            for (int sample = 0; sample < Samples; sample++)
            {
                double value = 0;
                for (int index = 0; index < frequencies.Length; index++)
                {
                    value += Math.Sin(2 * Math.PI * frequencies[index] * sample / Rate + index * 0.19) * 0.078;
                }
                value *= Math.Min(1, Math.Min(sample, Samples - 1 - sample) / 96d);
                source[sample * 2 + activeChannel] = (float)(Math.Round(value * 32767) / 32768);
                source[sample * 2 + 1 - activeChannel] = (float)(Math.Round(value * 0.05 * 32767) / 32768);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            AudioFormat format = new(Rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, ["front-left", "front-right"]);
            using AacEncoder encoder = AacEncoder.Create(format, new(1, Rate), allocator, new() { BitRate = BitRate });
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(encoder.Stream, allocator);
            double[] signal = new double[2];
            double[] error = new double[2];
            int packets = 0;
            int compared = 0;
            int position = 0;
            while (position < Samples)
            {
                int count = Math.Min(4096, Samples - position);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(count));
                source.AsSpan(position * 2, count * 2).CopyTo(MemoryMarshal.Cast<byte, float>(writable.Memory.Span));
                using MediaBuffer storage = writable.Publish();
                using AudioFrame frame = new(format, count, storage, new(position, new(1, Rate)));
                if (encoder.TrySend(frame) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("AAC encoding rejected drained quality input.");
                }
                position += count;
                Drain();
            }
            encoder.CompleteInput();
            Drain();
            if (packets != 107 || compared != Samples * 2 || encoder.InputSamplesPerChannel != Samples ||
                encoder.PrimingSamples != 1024 || encoder.PaddingSamples != 507 || encoder.Receive().Status != CodecOutputStatus.EndOfStream)
            {
                throw new InvalidDataException("AAC relative-quality encoding changed sample extent, timing or final drain.");
            }
            double loudSnr = 10 * Math.Log10(signal[activeChannel] / error[activeChannel]);
            double quietSnr = 10 * Math.Log10(signal[1 - activeChannel] / error[1 - activeChannel]);
            if (!double.IsFinite(loudSnr) || !double.IsFinite(quietSnr) || loudSnr < 45 || quietSnr < 36)
            {
                throw new InvalidDataException($"AAC unequal-channel quality differs: loud {loudSnr} dB; quiet {quietSnr} dB.");
            }
            decoder.Dispose();
            encoder.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("AAC relative-quality encoding retained media storage.");
            }

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
                    long start = (packets - 1L) * 1024;
                    if (packet.PresentationTime != new MediaTimestamp(start, new(1, Rate)) || packet.DecodeTime != packet.PresentationTime ||
                        packet.Duration != new MediaDuration(1024, new(1, Rate)) || packet.Data.Length * 8 > (long)BitRate * 1024 / Rate)
                    {
                        throw new InvalidDataException("AAC relative-quality allocation changed the packet clock or exceeded its bit ceiling.");
                    }
                    if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("AAC decoding rejected drained quality input.");
                    }
                    using AudioFrame frame = decoder.Receive().Value;
                    ReadOnlySpan<float> pcm = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                    for (int sample = 0; sample < frame.SamplesPerChannel; sample++)
                    {
                        for (int channel = 0; channel < 2; channel++)
                        {
                            double actual = pcm[sample * 2 + channel];
                            if (!double.IsFinite(actual))
                            {
                                throw new InvalidDataException("AAC relative-quality output is nonfinite.");
                            }
                            long original = start + sample;
                            if (original >= 0 && original < Samples)
                            {
                                double expected = source[(int)original * 2 + channel];
                                signal[channel] += expected * expected;
                                error[channel] += (actual - expected) * (actual - expected);
                                compared++;
                            }
                        }
                    }
                    packets++;
                }
            }
        }
    }
}
