// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Audio;

namespace Mediana.Formats.Audio
{
    /// <summary>Apple IMA4 audio described by the AIFF-C FORM type and COMM payload.</summary>
    public class AiffIma4Configuration
    {
        public const string CodecId = "adpcm-ima-qt";
        public const int ChannelPacketBytes = 34;
        public const int SamplesPerPacket = 64;

        private AiffIma4Configuration(AudioFormat format) => Format = format;

        public AudioFormat Format { get; }
        public int BlockAlignment => ChannelPacketBytes * Format.Channels.Count;

        public static AiffIma4Configuration? Read(ReadOnlySpan<byte> configuration)
        {
            if (configuration.Length < 26 || !configuration[..4].SequenceEqual("AIFC"u8) ||
                !configuration.Slice(22, 4).SequenceEqual("ima4"u8))
            {
                return null;
            }
            ReadOnlySpan<byte> common = PcmConfiguration.ReadAiffCommon(configuration, out _);
            int channels = BinaryPrimitives.ReadInt16BigEndian(common);
            int precision = BinaryPrimitives.ReadInt16BigEndian(common[6..]);
            if (channels <= 0 || precision is not (4 or 16))
            {
                throw new InvalidDataException("AIFF-C ima4 requires a positive channel count and 4- or 16-bit precision.");
            }
            string[] order = channels switch
            {
                1 => ["mono"],
                2 => ["front-left", "front-right"],
                _ => throw new NotSupportedException("AIFF-C ima4 supports mono and stereo channel layouts.")
            };
            int rate = PcmConfiguration.ReadAiffRate(common.Slice(8, 10))
                ?? throw new NotSupportedException("AIFF-C ima4 requires a positive integral sample rate.");
            return new(new(rate, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, order));
        }
    }
}
