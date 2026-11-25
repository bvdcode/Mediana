// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Audio;

namespace Mediana.Formats.Audio
{
    /// <summary>WAVE format and packet geometry for G.711 A-law and DVI/IMA ADPCM.</summary>
    public class WaveCompressedAudioConfiguration
    {
        public const string ALawCodecId = "g711-alaw";
        public const string ImaAdpcmCodecId = "adpcm-ima-wave";
        public const int ImaChannelHeaderBytes = 4;

        private WaveCompressedAudioConfiguration(WaveCompressedAudioEncoding encoding, AudioFormat decodedFormat,
            int blockAlignment, int samplesPerBlock, int codeBits)
        {
            Encoding = encoding;
            DecodedFormat = decodedFormat;
            BlockAlignment = blockAlignment;
            SamplesPerBlock = samplesPerBlock;
            CodeBits = codeBits;
        }

        public WaveCompressedAudioEncoding Encoding { get; }
        public AudioFormat DecodedFormat { get; }
        public int BlockAlignment { get; }
        public int SamplesPerBlock { get; }
        public int CodeBits { get; }
        public string CodecId => GetCodecId(Encoding);

        public static string GetCodecId(WaveCompressedAudioEncoding encoding) => encoding switch
        {
            WaveCompressedAudioEncoding.ALaw => ALawCodecId,
            WaveCompressedAudioEncoding.ImaAdpcm => ImaAdpcmCodecId,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        public static EncodedStreamFormat GetStreamFormat(WaveCompressedAudioEncoding encoding) => encoding switch
        {
            WaveCompressedAudioEncoding.ALaw => StreamFormats.WaveALaw,
            WaveCompressedAudioEncoding.ImaAdpcm => StreamFormats.WaveImaAdpcm,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        /// <summary>Reads a supported format or returns null for another format tag.</summary>
        public static WaveCompressedAudioConfiguration? ReadWaveFormat(ReadOnlySpan<byte> configuration)
        {
            if (configuration.Length < 16)
            {
                throw new InvalidDataException("The WAVE format is truncated.");
            }

            WaveCompressedAudioEncoding encoding = (WaveCompressedAudioEncoding)BinaryPrimitives.ReadUInt16LittleEndian(configuration);
            if (encoding is not (WaveCompressedAudioEncoding.ALaw or WaveCompressedAudioEncoding.ImaAdpcm))
            {
                return null;
            }
            if (configuration.Length == 17 || (configuration.Length >= 18 &&
                BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) > configuration.Length - 18))
            {
                throw new InvalidDataException("The WAVE format extension is truncated.");
            }

            int channels = BinaryPrimitives.ReadUInt16LittleEndian(configuration[2..]);
            uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(configuration[4..]);
            int blockAlignment = BinaryPrimitives.ReadUInt16LittleEndian(configuration[12..]);
            int codeBits = BinaryPrimitives.ReadUInt16LittleEndian(configuration[14..]);
            if (channels is < 1 or > 32 || sampleRate is 0 or > int.MaxValue)
            {
                throw new InvalidDataException("The compressed WAVE channel count or sample rate is invalid.");
            }

            int samplesPerBlock;
            switch (encoding)
            {
                case WaveCompressedAudioEncoding.ALaw:
                    if (codeBits != 8 || blockAlignment != channels)
                    {
                        throw new InvalidDataException("G.711 A-law WAVE requires one byte per channel in a sample block.");
                    }
                    samplesPerBlock = 1;
                    break;
                case WaveCompressedAudioEncoding.ImaAdpcm:
                    if (codeBits is not (3 or 4))
                    {
                        throw new NotSupportedException("WAVE IMA ADPCM requires 3- or 4-bit codes.");
                    }
                    if (configuration.Length < 20 || BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) < 2)
                    {
                        throw new InvalidDataException("The WAVE IMA ADPCM samples-per-block field is missing.");
                    }
                    samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(configuration[18..]);
                    if (samplesPerBlock != GetImaBlockSamples(blockAlignment, channels, codeBits))
                    {
                        throw new InvalidDataException("The WAVE IMA ADPCM sample count does not match its block alignment.");
                    }
                    break;
                default:
                    throw new InvalidOperationException("The compressed WAVE encoding is invalid.");
            }

            AudioFormat format = new((int)sampleRate, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved,
                channels switch
                {
                    1 => ["mono"],
                    2 => ["front-left", "front-right"],
                    _ => Enumerable.Range(1, channels).Select(static channel => $"discrete-{channel}").ToArray()
                });
            return new(encoding, format, blockAlignment, samplesPerBlock, codeBits);
        }

        /// <summary>Gets the sample frames stored in whole blocks and an IMA tail ending at a complete group.</summary>
        public long GetSampleCapacity(long bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            long samples = checked(bytes / BlockAlignment * SamplesPerBlock);
            int tail = (int)(bytes % BlockAlignment);
            if (tail == 0)
            {
                return samples;
            }
            if (Encoding == WaveCompressedAudioEncoding.ALaw)
            {
                throw new InvalidDataException("WAVE A-law data ends in an incomplete sample block.");
            }
            return checked(samples + GetImaBlockSamples(tail, DecodedFormat.Channels.Count, CodeBits));
        }

        private static int GetImaBlockSamples(int bytes, int channels, int codeBits)
        {
            int payload = bytes - ImaChannelHeaderBytes * channels;
            int groupBytes = (codeBits == 4 ? 4 : 12) * channels;
            if (payload < 0 || payload % groupBytes != 0)
            {
                throw new InvalidDataException("A WAVE IMA ADPCM block requires a header and complete code groups for every channel.");
            }
            return 1 + payload / channels * 8 / codeBits;
        }
    }
}
