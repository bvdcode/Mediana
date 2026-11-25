// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Audio;

namespace Mediana.Formats.Audio
{
    /// <summary>WAVE format and packet geometry for G.711, DVI/IMA ADPCM and Microsoft ADPCM.</summary>
    public class WaveCompressedAudioConfiguration
    {
        public const string ALawCodecId = "g711-alaw";
        public const string MuLawCodecId = "g711-mulaw";
        public const string ImaAdpcmCodecId = "adpcm-ima-wave";
        public const string MicrosoftAdpcmCodecId = "adpcm-ms";
        public const int ImaChannelHeaderBytes = 4;
        public const int MicrosoftAdpcmChannelHeaderBytes = 7;
        private readonly (short First, short Second)[]? microsoftAdpcmCoefficients;

        private WaveCompressedAudioConfiguration(WaveCompressedAudioEncoding encoding, AudioFormat decodedFormat,
            int blockAlignment, int samplesPerBlock, int codeBits, (short First, short Second)[]? microsoftAdpcmCoefficients)
        {
            Encoding = encoding;
            DecodedFormat = decodedFormat;
            BlockAlignment = blockAlignment;
            SamplesPerBlock = samplesPerBlock;
            CodeBits = codeBits;
            this.microsoftAdpcmCoefficients = microsoftAdpcmCoefficients;
        }

        public WaveCompressedAudioEncoding Encoding { get; }
        public AudioFormat DecodedFormat { get; }
        public int BlockAlignment { get; }
        public int SamplesPerBlock { get; }
        public int CodeBits { get; }
        public string CodecId => GetCodecId(Encoding);
        public bool CanTrimFinalBlock => Encoding is WaveCompressedAudioEncoding.ImaAdpcm or WaveCompressedAudioEncoding.MicrosoftAdpcm;

        public static string GetCodecId(WaveCompressedAudioEncoding encoding) => encoding switch
        {
            WaveCompressedAudioEncoding.ALaw => ALawCodecId,
            WaveCompressedAudioEncoding.MuLaw => MuLawCodecId,
            WaveCompressedAudioEncoding.ImaAdpcm => ImaAdpcmCodecId,
            WaveCompressedAudioEncoding.MicrosoftAdpcm => MicrosoftAdpcmCodecId,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        public static EncodedStreamFormat GetStreamFormat(WaveCompressedAudioEncoding encoding) => encoding switch
        {
            WaveCompressedAudioEncoding.ALaw => StreamFormats.WaveALaw,
            WaveCompressedAudioEncoding.MuLaw => StreamFormats.WaveMuLaw,
            WaveCompressedAudioEncoding.ImaAdpcm => StreamFormats.WaveImaAdpcm,
            WaveCompressedAudioEncoding.MicrosoftAdpcm => StreamFormats.WaveMicrosoftAdpcm,
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
            if (encoding is not (WaveCompressedAudioEncoding.ALaw or WaveCompressedAudioEncoding.MuLaw
                or WaveCompressedAudioEncoding.ImaAdpcm or WaveCompressedAudioEncoding.MicrosoftAdpcm))
            {
                return null;
            }
            int extensionBytes = configuration.Length >= 18 ? BinaryPrimitives.ReadUInt16LittleEndian(configuration[16..]) : 0;
            if (configuration.Length == 17 || (configuration.Length >= 18 && extensionBytes > configuration.Length - 18))
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
            (short First, short Second)[]? coefficients = null;
            switch (encoding)
            {
                case WaveCompressedAudioEncoding.ALaw:
                case WaveCompressedAudioEncoding.MuLaw:
                    if (codeBits != 8 || blockAlignment != channels)
                    {
                        throw new InvalidDataException("G.711 WAVE requires one byte per channel in a sample block.");
                    }
                    samplesPerBlock = 1;
                    break;
                case WaveCompressedAudioEncoding.ImaAdpcm:
                    if (codeBits is not (3 or 4))
                    {
                        throw new NotSupportedException("WAVE IMA ADPCM requires 3- or 4-bit codes.");
                    }
                    if (extensionBytes < 2)
                    {
                        throw new InvalidDataException("The WAVE IMA ADPCM samples-per-block field is missing.");
                    }
                    samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(configuration[18..]);
                    if (samplesPerBlock != GetImaBlockSamples(blockAlignment, channels, codeBits))
                    {
                        throw new InvalidDataException("The WAVE IMA ADPCM sample count does not match its block alignment.");
                    }
                    break;
                case WaveCompressedAudioEncoding.MicrosoftAdpcm:
                    if (codeBits != 4 || channels > 2)
                    {
                        throw new NotSupportedException("WAVE Microsoft ADPCM requires 4-bit mono or stereo audio.");
                    }
                    if (extensionBytes < 4)
                    {
                        throw new InvalidDataException("The WAVE Microsoft ADPCM format extension is truncated.");
                    }
                    samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(configuration[18..]);
                    int blockCapacity = GetMicrosoftAdpcmBlockSamples(blockAlignment, channels);
                    if (samplesPerBlock < 2 || samplesPerBlock > blockCapacity)
                    {
                        throw new InvalidDataException("The WAVE Microsoft ADPCM sample count exceeds the block capacity or omits its initial samples.");
                    }
                    coefficients = ReadMicrosoftAdpcmCoefficients(configuration.Slice(20, extensionBytes - 2));
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
            return new(encoding, format, blockAlignment, samplesPerBlock, codeBits, coefficients);
        }

        /// <summary>Gets the sample capacity of the stored blocks, including a shorter final ADPCM block if present.</summary>
        public long GetSampleCapacity(long bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            long samples = checked(bytes / BlockAlignment * SamplesPerBlock);
            int tail = (int)(bytes % BlockAlignment);
            if (tail == 0)
            {
                return samples;
            }
            int channels = DecodedFormat.Channels.Count;
            int tailSamples = Encoding switch
            {
                WaveCompressedAudioEncoding.ImaAdpcm => GetImaBlockSamples(tail, channels, CodeBits),
                WaveCompressedAudioEncoding.MicrosoftAdpcm => Math.Min(SamplesPerBlock, GetMicrosoftAdpcmBlockSamples(tail, channels)),
                WaveCompressedAudioEncoding.ALaw or WaveCompressedAudioEncoding.MuLaw =>
                    throw new InvalidDataException("WAVE G.711 data ends in an incomplete sample block."),
                _ => throw new InvalidOperationException("The compressed WAVE encoding is invalid.")
            };
            return checked(samples + tailSamples);
        }

        /// <summary>Checks a WAVE fact count or packet duration against the final coded block.</summary>
        public long GetSampleCount(long bytes, long declaredSamples)
        {
            long capacity = GetSampleCapacity(bytes);
            long minimum = bytes == 0 ? 0 : (bytes - 1) / BlockAlignment * SamplesPerBlock + 1;
            if (!CanTrimFinalBlock || declaredSamples < minimum || declaredSamples > capacity)
            {
                throw new InvalidDataException("The WAVE ADPCM sample count does not match its data blocks.");
            }
            return declaredSamples;
        }

        public (short First, short Second) GetMicrosoftAdpcmCoefficients(int predictor)
        {
            if (microsoftAdpcmCoefficients is null || (uint)predictor >= (uint)microsoftAdpcmCoefficients.Length)
            {
                throw new InvalidDataException("The WAVE Microsoft ADPCM block predictor is out of range.");
            }
            return microsoftAdpcmCoefficients[predictor];
        }

        private static (short First, short Second)[] ReadMicrosoftAdpcmCoefficients(ReadOnlySpan<byte> extension)
        {
            const int pairBytes = sizeof(short) * 2;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(extension);
            ReadOnlySpan<byte> table = extension[sizeof(ushort)..];
            if (count is < 7 or > 256 || count > table.Length / pairBytes)
            {
                throw new InvalidDataException("The WAVE Microsoft ADPCM coefficient table is invalid or truncated.");
            }
            (short First, short Second)[] coefficients = new (short, short)[count];
            for (int predictor = 0; predictor < count; predictor++)
            {
                ReadOnlySpan<byte> pair = table[(predictor * pairBytes)..];
                coefficients[predictor] = (BinaryPrimitives.ReadInt16LittleEndian(pair), BinaryPrimitives.ReadInt16LittleEndian(pair[sizeof(short)..]));
            }
            return coefficients;
        }

        private static int GetMicrosoftAdpcmBlockSamples(int bytes, int channels)
        {
            int payload = bytes - MicrosoftAdpcmChannelHeaderBytes * channels;
            if (payload < 0)
            {
                throw new InvalidDataException("A WAVE Microsoft ADPCM block requires a header for each channel.");
            }
            return 2 + payload * 2 / channels;
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
