// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Pcm
{
    internal static class ImaAdpcmWaveBlockDecoder
    {
        private const int WordBytes = sizeof(uint);
        private static ReadOnlySpan<short> Steps =>
        [
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
            34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
            3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        ];
        private static ReadOnlySpan<sbyte> IndexDeltas4 => [-1, -1, -1, -1, 2, 4, 6, 8];
        private static ReadOnlySpan<sbyte> IndexDeltas3 => [-1, -1, 1, 2];

        public static void DecodeBlock(ReadOnlySpan<byte> block, Span<short> destination, int channels, int bits,
            CancellationToken cancellationToken)
        {
            int wordStride = WordBytes * channels;
            int wordsPerGroup = bits == 4 ? 1 : 3;
            int codesPerGroup = WordBytes * 8 * wordsPerGroup / bits;
            int signBit = 1 << (bits - 1);
            int samplesPerChannel = destination.Length / channels;
            ReadOnlySpan<sbyte> indexDeltas = bits == 4 ? IndexDeltas4 : IndexDeltas3;
            for (int channel = 0; channel < channels; channel++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int offset = channel * WaveCompressedAudioConfiguration.ImaChannelHeaderBytes;
                int predictor = BinaryPrimitives.ReadInt16LittleEndian(block[offset..]);
                int index = block[offset + sizeof(short)];
                if (index >= Steps.Length)
                {
                    throw new InvalidDataException("The WAVE IMA ADPCM step index is out of range.");
                }
                destination[channel] = (short)predictor;
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
                            destination[output] = Expand((int)(codes & 15), signBit, indexDeltas, ref predictor, ref index);
                        }
                    }
                    else
                    {
                        UInt128 codes = BinaryPrimitives.ReadUInt32LittleEndian(block[offset..]);
                        codes |= (UInt128)BinaryPrimitives.ReadUInt32LittleEndian(block[(offset + wordStride)..]) << 32;
                        codes |= (UInt128)BinaryPrimitives.ReadUInt32LittleEndian(block[(offset + 2 * wordStride)..]) << 64;
                        for (int i = 0; i < count; i++, output += channels, codes >>= 3)
                        {
                            destination[output] = Expand((int)(codes & 7), signBit, indexDeltas, ref predictor, ref index);
                        }
                    }
                }
            }
        }

        private static short Expand(int code, int signBit, ReadOnlySpan<sbyte> indexDeltas, ref int predictor, ref int index)
        {
            int step = Steps[index];
            int magnitudeCode = code & (signBit - 1);
            int difference = step >> (signBit == 8 ? 3 : 2);
            if ((magnitudeCode & (signBit >> 1)) != 0)
            {
                difference += step;
            }
            if ((magnitudeCode & (signBit >> 2)) != 0)
            {
                difference += step >> 1;
            }
            if (signBit == 8 && (magnitudeCode & 1) != 0)
            {
                difference += step >> 2;
            }
            predictor = Math.Clamp(predictor + ((code & signBit) == 0 ? difference : -difference), short.MinValue, short.MaxValue);
            index = Math.Clamp(index + indexDeltas[magnitudeCode], 0, Steps.Length - 1);
            return (short)predictor;
        }
    }
}
