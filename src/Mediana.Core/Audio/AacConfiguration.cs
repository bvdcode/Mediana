// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Audio
{
    /// <summary>AAC-LC configuration for 1024-sample frames and an indexed sampling frequency.</summary>
    public class AacConfiguration
    {
        public const string CodecId = "aac";

        private AacConfiguration(int frequencyIndex, int channelConfiguration)
        {
            FrequencyIndex = frequencyIndex;
            ChannelConfiguration = channelConfiguration;
            SampleRate = SampleRates[frequencyIndex];
        }

        private static ReadOnlySpan<int> SampleRates =>
            [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

        public int FrequencyIndex { get; }
        public int SampleRate { get; }
        public int ChannelConfiguration { get; }
        public int ChannelCount => ChannelConfiguration == 7 ? 8 : ChannelConfiguration;
        public int SamplesPerFrame => 1024;

        /// <summary>Returns null for an unsupported configuration and throws for truncated or reserved syntax.</summary>
        public static AacConfiguration? ReadAudioSpecificConfig(ReadOnlySpan<byte> data, out string? unsupportedReason)
        {
            int position = 0;
            int objectType = Read(data, ref position, 5);
            if (objectType == 31)
            {
                objectType = 32 + Read(data, ref position, 6);
            }
            if (objectType != 2)
            {
                unsupportedReason = "Only the AAC-LC audio object type is supported.";
                return null;
            }
            int frequency = Read(data, ref position, 4);
            if (frequency is 13 or 14)
            {
                throw new InvalidDataException("The AAC sampling frequency index is reserved.");
            }
            if (frequency == 15)
            {
                Read(data, ref position, 24);
                unsupportedReason = "Explicit AAC sampling frequencies are not supported.";
                return null;
            }
            int channels = Read(data, ref position, 4);
            if (channels is < 1 or > 7)
            {
                unsupportedReason = "AAC requires a supported channel configuration without a program config element.";
                return null;
            }
            if (Read(data, ref position, 1) != 0)
            {
                unsupportedReason = "AAC frames with 960 samples are not supported.";
                return null;
            }
            if (Read(data, ref position, 1) != 0)
            {
                Read(data, ref position, 14);
                unsupportedReason = "AAC core coder dependencies are not supported.";
                return null;
            }
            if (Read(data, ref position, 1) != 0 && Read(data, ref position, 1) != 0)
            {
                unsupportedReason = "AAC configuration extension flag 3 is not supported.";
                return null;
            }
            if ((long)data.Length * 8 - position >= 16)
            {
                if (Read(data, ref position, 11) != 0x2b7 || Read(data, ref position, 5) != 5)
                {
                    unsupportedReason = "The AAC configuration extension is not supported.";
                    return null;
                }
                if (Read(data, ref position, 1) != 0)
                {
                    unsupportedReason = "AAC spectral band replication is not supported.";
                    return null;
                }
            }
            if ((long)data.Length * 8 - position > 7)
            {
                unsupportedReason = "The AAC configuration contains additional unsupported data.";
                return null;
            }
            while (position < (long)data.Length * 8)
            {
                if (Read(data, ref position, 1) != 0)
                {
                    throw new InvalidDataException("The AAC configuration padding is not zero.");
                }
            }
            unsupportedReason = null;
            return new AacConfiguration(frequency, channels);
        }

        private static int Read(ReadOnlySpan<byte> data, ref int position, int count)
        {
            if ((long)position + count > (long)data.Length * 8)
            {
                throw new InvalidDataException("The AAC AudioSpecificConfig is truncated.");
            }
            int result = 0;
            for (int index = 0; index < count; index++, position++)
            {
                result = result << 1 | data[position >> 3] >> (7 - (position & 7)) & 1;
            }
            return result;
        }
    }
}
