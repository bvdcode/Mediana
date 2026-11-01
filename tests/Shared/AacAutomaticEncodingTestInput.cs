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
    internal static class AacAutomaticEncodingTestInput
    {
        public static void Verify(int rate, int channels, int samples, int attackOffset = 576, int attackChannel = 0, int? bitRate = null)
        {
            int attack = Math.Min(samples - 1, 2048 + attackOffset);
            byte[][] complete = Encode([samples]);
            byte[][] split = Encode([1, 17, 1023, 2049]);
            if (complete.Length != split.Length)
            {
                throw new InvalidDataException("AAC packet count depends on input block sizes.");
            }
            for (int packet = 0; packet < complete.Length; packet++)
            {
                if (!complete[packet].AsSpan().SequenceEqual(split[packet]))
                {
                    throw new InvalidDataException("AAC window decisions or packets depend on input block sizes.");
                }
            }

            byte[][] Encode(int[] blocks)
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                AudioFormat format = new(rate, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved,
                    channels == 1 ? ["front-center"] : ["front-left", "front-right"]);
                int ceiling = bitRate ?? rate * channels * 2;
                using AacEncoder encoder = AacEncoder.Create(format, new(1, rate), allocator, new() { BitRate = ceiling });
                using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(encoder.Stream, allocator);
                List<byte[]> packets = [];
                int position = 0;
                int block = 0;
                int previousSequence = -1;
                while (position < samples)
                {
                    int count = Math.Min(blocks[block++ % blocks.Length], samples - position);
                    using WritableMediaBuffer storage = allocator.Allocate(format.GetBufferSize(count));
                    Span<float> pcm = MemoryMarshal.Cast<byte, float>(storage.Memory.Span);
                    for (int sample = 0; sample < count; sample++)
                    {
                        for (int channel = 0; channel < channels; channel++)
                        {
                            pcm[sample * channels + channel] = position + sample == attack && channel == attackChannel
                                ? 0.9f : (float)(0.02 * Math.Sin((position + sample) * 0.03));
                        }
                    }
                    using MediaBuffer buffer = storage.Publish();
                    using AudioFrame frame = new(format, count, buffer, new(position, new(1, rate)));
                    if (encoder.TrySend(frame) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("AAC rejected drained input while collecting lookahead.");
                    }
                    position += count;
                    Drain();
                }
                encoder.CompleteInput();
                Drain();
                if (encoder.Receive().Status != CodecOutputStatus.EndOfStream ||
                    packets.Count != (samples + 1023) / 1024 + 1 || encoder.InputSamplesPerChannel != samples ||
                    encoder.PrimingSamples != 1024 || encoder.PaddingSamples != (1024 - samples % 1024) % 1024)
                {
                    throw new InvalidDataException("AAC lookahead changed complete-input duration, priming or padding.");
                }
                encoder.Reset();
                if (encoder.Receive().Status != CodecOutputStatus.InputRequired)
                {
                    throw new InvalidOperationException("AAC reset retained buffered lookahead.");
                }
                encoder.CompleteInput();
                if (encoder.Receive().Status != CodecOutputStatus.EndOfStream)
                {
                    throw new InvalidOperationException("An empty AAC input produced a packet.");
                }
                decoder.Dispose();
                encoder.Dispose();
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("AAC automatic window encoding retained media storage.");
                }
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
                        int index = packets.Count;
                        if (packet.Data.Length * 8 > (long)ceiling * 1024 / rate + 1 ||
                            packet.PresentationTime != new MediaTimestamp((index - 1) * 1024, new(1, rate)) ||
                            packet.DecodeTime != packet.PresentationTime || packet.Duration != new MediaDuration(1024, new(1, rate)))
                        {
                            throw new InvalidDataException("AAC lookahead changed the sample clock or exceeded its frame ceiling.");
                        }
                        int sequence = ReadSequence(packet.Data.Span, channels);
                        bool legal = previousSequence switch
                        {
                            -1 => sequence is 0 or 1 or 2,
                            0 or 3 => sequence is 0 or 1,
                            1 or 2 => sequence is 2 or 3,
                            _ => false
                        };
                        if (!legal || (index >= attack / 1024 && index <= attack / 1024 + 1 && sequence != 2))
                        {
                            throw new InvalidDataException("AAC emitted an invalid transition or left an attack in a long window.");
                        }
                        previousSequence = sequence;
                        packets.Add(packet.Data.ToArray());
                        if (decoder.TrySend(packet) != CodecInputStatus.Accepted)
                        {
                            throw new InvalidOperationException("AAC decoder rejected the next automatic-window packet.");
                        }
                        CodecOutput<AudioFrame> decoded = decoder.Receive();
                        if (decoded.Status != CodecOutputStatus.Available)
                        {
                            throw new InvalidDataException("AAC automatic-window packet did not decode a complete frame.");
                        }
                        using AudioFrame result = decoded.Value;
                        if (result.SamplesPerChannel != 1024)
                        {
                            throw new InvalidDataException("AAC automatic-window output has an invalid sample count.");
                        }
                        foreach (float value in MemoryMarshal.Cast<byte, float>(result.GetPlane(0).Span))
                        {
                            if (!float.IsFinite(value))
                            {
                                throw new InvalidDataException("AAC automatic-window PCM is not finite.");
                            }
                        }
                    }
                }
            }
        }

        private static int ReadSequence(ReadOnlySpan<byte> data, int channels)
        {
            AacBitReader bits = new(data);
            int element = bits.Read(3);
            bits.Skip(4);
            if (channels == 1)
            {
                if (element != 0)
                {
                    throw new InvalidDataException("AAC mono output is not a single-channel element.");
                }
                bits.Skip(8);
            }
            else if (element != 1 || bits.Read(1) != 1)
            {
                throw new InvalidDataException("AAC stereo output does not share its window sequence.");
            }
            if (bits.Read(1) != 0)
            {
                throw new InvalidDataException("AAC reserved window bit is not zero.");
            }
            return bits.Read(2);
        }
    }
}
