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
        private static readonly short[] muLawSamples = CreateMuLawTable();

        protected override AudioFrame Decode(MediaPacket packet, int maxSamples, CancellationToken cancellationToken)
        {
            long samples = GetSampleCount(packet);
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
                    DecodeG711(packet.Data.Span, output, aLawSamples, cancellationToken);
                    break;
                case WaveCompressedAudioEncoding.MuLaw:
                    DecodeG711(packet.Data.Span, output, muLawSamples, cancellationToken);
                    break;
                case WaveCompressedAudioEncoding.ImaAdpcm:
                case WaveCompressedAudioEncoding.MicrosoftAdpcm:
                    DecodeAdpcm(packet.Data.Span, output, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("The compressed WAVE encoding is invalid.");
            }
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(format, (int)samples, buffer, packet.PresentationTime);
        }

        private long GetSampleCount(MediaPacket packet)
        {
            if (!configuration.CanTrimFinalBlock || packet.Duration is not MediaDuration duration)
            {
                return configuration.GetSampleCapacity(packet.Data.Length);
            }
            TimeBase sampleClock = new(1, configuration.DecodedFormat.SampleRate);
            MediaTimestamp end = new(duration.Value, duration.TimeBase);
            MediaTimestamp sampleEnd = end.Rescale(sampleClock, TimestampRounding.TowardZero);
            if (sampleEnd != end)
            {
                throw new InvalidDataException("The WAVE ADPCM packet duration must be a whole number of sample frames.");
            }
            return configuration.GetSampleCount(packet.Data.Length, sampleEnd.Value);
        }

        private void DecodeAdpcm(ReadOnlySpan<byte> source, Span<short> destination, CancellationToken cancellationToken)
        {
            int channels = configuration.DecodedFormat.Channels.Count;
            int interleavedSamplesPerBlock = configuration.SamplesPerBlock * channels;
            while (!source.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int blockBytes = Math.Min(configuration.BlockAlignment, source.Length);
                int outputSamples = Math.Min(interleavedSamplesPerBlock, destination.Length);
                ReadOnlySpan<byte> block = source[..blockBytes];
                Span<short> output = destination[..outputSamples];
                switch (configuration.Encoding)
                {
                    case WaveCompressedAudioEncoding.ImaAdpcm:
                        ImaAdpcmWaveBlockDecoder.DecodeBlock(block, output, channels, configuration.CodeBits, cancellationToken);
                        break;
                    case WaveCompressedAudioEncoding.MicrosoftAdpcm:
                        MicrosoftAdpcmWaveBlockDecoder.DecodeBlock(block, output, configuration);
                        break;
                    default:
                        throw new InvalidOperationException("The WAVE ADPCM block encoding is invalid.");
                }
                source = source[blockBytes..];
                destination = destination[outputSamples..];
            }
        }

        private static void DecodeG711(ReadOnlySpan<byte> source, Span<short> output, ReadOnlySpan<short> sampleTable,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if ((i & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                output[i] = sampleTable[source[i]];
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

        private static short[] CreateMuLawTable()
        {
            const int bias = 0x84;
            short[] samples = new short[256];
            for (int i = 0; i < samples.Length; i++)
            {
                int code = i ^ 0xff;
                int segment = (code >> 4) & 7;
                int magnitude = ((((code & 15) << 3) + bias) << segment) - bias;
                samples[i] = (short)((code & 0x80) == 0 ? magnitude : -magnitude);
            }
            return samples;
        }
    }
}
