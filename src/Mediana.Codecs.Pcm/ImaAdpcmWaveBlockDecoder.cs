// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    internal static class ImaAdpcmWaveBlockDecoder
    {
        private const int WordBytes = sizeof(uint);

        public static void DecodeBlock(ReadOnlySpan<byte> block, Span<short> destination, int channels, int bits,
            CancellationToken cancellationToken)
        {
            int wordStride = WordBytes * channels;
            int wordsPerGroup = bits == 4 ? 1 : 3;
            int codesPerGroup = WordBytes * 8 * wordsPerGroup / bits;
            int samplesPerChannel = destination.Length / channels;
            for (int channel = 0; channel < channels; channel++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int offset = channel * WaveCompressedAudioConfiguration.ImaChannelHeaderBytes;
                short sample = BinaryPrimitives.ReadInt16LittleEndian(block[offset..]);
                int index = block[offset + sizeof(short)];
                if (index > ImaAdpcmPredictor.MaximumIndex)
                {
                    throw new InvalidDataException("The WAVE IMA ADPCM step index is out of range.");
                }
                ImaAdpcmPredictor predictor = new(sample, index);
                destination[channel] = sample;
                int output = channel + channels;
                offset += wordStride;
                for (int first = 1; first < samplesPerChannel; first += codesPerGroup, offset += wordsPerGroup * wordStride)
                {
                    int count = Math.Min(codesPerGroup, samplesPerChannel - first);
                    if (bits == 4)
                    {
                        uint codes = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
                        for (int i = 0; i < count; i++, output += channels, codes >>= 4)
                        {
                            destination[output] = predictor.Decode4((int)(codes & 15));
                        }
                    }
                    else
                    {
                        UInt128 codes = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
                        codes |= (UInt128)BinaryPrimitives.ReadUInt32LittleEndian(block[(offset + wordStride)..]) << 32;
                        codes |= (UInt128)BinaryPrimitives.ReadUInt32LittleEndian(block[(offset + 2 * wordStride)..]) << 64;
                        for (int i = 0; i < count; i++, output += channels, codes >>= 3)
                        {
                            destination[output] = predictor.Decode3((int)(codes & 7));
                        }
                    }
                }
            }
        }
    }
}
