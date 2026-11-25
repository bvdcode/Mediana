// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    internal class WaveCompressedAudioDecoder(int streamIndex, WaveCompressedAudioConfiguration configuration,
        MediaBufferAllocator allocator, int maxSamplesPerFrame) : PcmPacketDecoder(streamIndex, maxSamplesPerFrame)
    {
        private static readonly short[] aLawSamples = CreateALawTable();

        protected override AudioFrame Decode(MediaPacket packet, int maxSamples, CancellationToken cancellationToken)
        {
            long sampleCapacity = configuration.GetSampleCapacity(packet.Data.Length);
            long samples = GetSampleCount(packet, sampleCapacity);
            if (samples > maxSamples)
            {
                throw new NotSupportedException("The compressed WAVE packet exceeds the configured sample limit.");
            }

            AudioFormat format = configuration.DecodedFormat;
            using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize((int)samples));
            Span<short> output = MemoryMarshal.Cast<byte, short>(writable.Memory.Span);
            switch (configuration.Encoding)
            {
                case WaveCompressedAudioEncoding.ALaw:
                    DecodeALaw(packet.Data.Span, output, cancellationToken);
                    break;
                case WaveCompressedAudioEncoding.ImaAdpcm:
                    ImaAdpcmWaveBlockDecoder.Decode(packet.Data.Span, output, configuration, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("The compressed WAVE encoding is invalid.");
            }
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, (int)samples, buffer, packet.PresentationTime);
        }

        private long GetSampleCount(MediaPacket packet, long capacity)
        {
            if (configuration.Encoding != WaveCompressedAudioEncoding.ImaAdpcm || packet.Duration is not MediaDuration duration)
            {
                return capacity;
            }
            TimeBase sampleClock = new(1, configuration.DecodedFormat.SampleRate);
            MediaTimestamp end = new(duration.Value, duration.TimeBase);
            MediaTimestamp sampleEnd = end.Rescale(sampleClock, TimestampRounding.TowardZero);
            long firstSampleInLastBlock = (packet.Data.Length - 1L) / configuration.BlockAlignment * configuration.SamplesPerBlock + 1;
            if (sampleEnd != end || sampleEnd.Value < firstSampleInLastBlock || sampleEnd.Value > capacity)
            {
                throw new InvalidDataException("The WAVE IMA ADPCM packet duration does not match its data blocks.");
            }
            return sampleEnd.Value;
        }

        private static void DecodeALaw(ReadOnlySpan<byte> source, Span<short> output, CancellationToken cancellationToken)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if ((i & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                output[i] = aLawSamples[source[i]];
            }
        }

        private static short[] CreateALawTable()
        {
            short[] samples = new short[256];
            for (int i = 0; i < samples.Length; i++)
            {
                int code = i ^ 0x55;
                int segment = (code >> 4) & 7;
                int quantized = ((code & 15) << 4) + 8;
                int magnitude = segment == 0 ? quantized : (quantized + 256) << (segment - 1);
                samples[i] = (short)((code & 0x80) == 0 ? -magnitude : magnitude);
            }
            return samples;
        }
    }
}
