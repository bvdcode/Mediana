// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    internal static class MicrosoftAdpcmWaveBlockDecoder
    {
        public static void DecodeBlock(ReadOnlySpan<byte> block, Span<short> output, WaveCompressedAudioConfiguration configuration)
        {
            int channels = configuration.DecodedFormat.Channels.Count;
            ReadOnlySpan<byte> codes = block[(channels * WaveCompressedAudioConfiguration.MicrosoftAdpcmChannelHeaderBytes)..];
            MicrosoftAdpcmChannel first = ReadChannel(block, output, configuration, 0, channels);
            if (channels == 1)
            {
                DecodeMono(codes, output, ref first);
                return;
            }

            MicrosoftAdpcmChannel second = ReadChannel(block, output, configuration, 1, channels);
            for (int offset = 4, index = 0; offset < output.Length; offset += 2, index++)
            {
                byte code = codes[index];
                output[offset] = first.Decode(code >> 4);
                output[offset + 1] = second.Decode(code & 15);
            }
        }

        private static MicrosoftAdpcmChannel ReadChannel(ReadOnlySpan<byte> block, Span<short> output,
            WaveCompressedAudioConfiguration configuration, int channel, int channels)
        {
            (short first, short second) = configuration.GetMicrosoftAdpcmCoefficients(block[channel]);
            int delta = BinaryPrimitives.ReadInt16LittleEndian(block[(channels + channel * sizeof(short))..]);
            if (delta < 0)
            {
                throw new InvalidDataException("The WAVE Microsoft ADPCM initial delta is negative.");
            }
            short secondSample = BinaryPrimitives.ReadInt16LittleEndian(block[(channels * 3 + channel * sizeof(short))..]);
            short firstSample = BinaryPrimitives.ReadInt16LittleEndian(block[(channels * 5 + channel * sizeof(short))..]);
            output[channel] = firstSample;
            if (output.Length > channels)
            {
                output[channels + channel] = secondSample;
            }
            return new(first, second, delta, firstSample, secondSample);
        }

        private static void DecodeMono(ReadOnlySpan<byte> codes, Span<short> output, ref MicrosoftAdpcmChannel channel)
        {
            int offset = 2;
            int index = 0;
            for (; offset + 1 < output.Length; offset += 2, index++)
            {
                byte code = codes[index];
                output[offset] = channel.Decode(code >> 4);
                output[offset + 1] = channel.Decode(code & 15);
            }
            if (offset < output.Length)
            {
                output[offset] = channel.Decode(codes[index] >> 4);
            }
        }
    }
}
