// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>AAC-LC core configuration for 1024-sample frames, including optional SBR/PS signaling.</summary>
    public class AacConfiguration
    {
        public const string CodecId = "aac";
        private readonly byte[]? serialized;

        private AacConfiguration(int frequencyIndex, int channelConfiguration, bool? sbr = null,
            int? extensionSampleRate = null, bool parametricStereo = false, byte[]? serialized = null)
        {
            FrequencyIndex = frequencyIndex;
            ChannelConfiguration = channelConfiguration;
            SampleRate = SampleRates[frequencyIndex];
            SpectralBandReplication = sbr;
            ExtensionSampleRate = extensionSampleRate;
            ParametricStereo = parametricStereo;
            this.serialized = serialized;
        }

        internal static ReadOnlySpan<int> SampleRates =>
            [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];

        public int FrequencyIndex { get; }
        public int SampleRate { get; }
        public int ChannelConfiguration { get; }
        public int ChannelCount => ChannelConfiguration == 7 ? 8 : ChannelConfiguration;
        public int SamplesPerFrame => 1024;
        /// <summary>Null means no out-of-band declaration; SBR may still be present in raw data.</summary>
        public bool? SpectralBandReplication { get; }
        public int? ExtensionSampleRate { get; }
        public bool ParametricStereo { get; }
        public int OutputSampleRate => ExtensionSampleRate ?? SampleRate;
        public int OutputSamplesPerFrame => checked(SamplesPerFrame * (OutputSampleRate / SampleRate));

        public static AacConfiguration Create(int sampleRate, int channelConfiguration)
        {
            int frequency = SampleRates.IndexOf(sampleRate);
            if (frequency < 0 || channelConfiguration is < 1 or > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "AAC-LC requires an indexed sample rate and a channel configuration from 1 to 7.");
            }
            return new(frequency, channelConfiguration);
        }

        public byte[] ToAudioSpecificConfig()
        {
            if (serialized is not null) { return (byte[])serialized.Clone(); }
            int value = (2 << 11) | (FrequencyIndex << 7) | (ChannelConfiguration << 3);
            return [(byte)(value >> 8), (byte)value];
        }

        /// <summary>Returns null for an unsupported configuration and throws for truncated or reserved syntax.
        /// Parsed enhancement signaling describes the stream; it does not imply decoder support.</summary>
        public static AacConfiguration? ReadAudioSpecificConfig(ReadOnlySpan<byte> data, out string? unsupportedReason)
        {
            int position = 0;
            int objectType = ReadObjectType(data, ref position);
            if (objectType is not (2 or 5 or 29))
            {
                unsupportedReason = "Only an AAC-LC core with optional SBR/PS signaling is supported.";
                return null;
            }
            int frequency = ReadFrequency(data, ref position);
            if (frequency < 0)
            {
                unsupportedReason = "The explicit AAC core frequency has no supported spectral band tables.";
                return null;
            }
            int channels = Read(data, ref position, 4);
            if (channels is < 1 or > 7)
            {
                unsupportedReason = "AAC requires a supported channel configuration without a program config element.";
                return null;
            }
            bool? sbr = null;
            int? extensionRate = null;
            bool ps = objectType == 29;
            if (objectType is 5 or 29)
            {
                sbr = true;
                int extensionFrequency = ReadFrequency(data, ref position);
                if (extensionFrequency < 0)
                {
                    unsupportedReason = "The explicit SBR output frequency is not supported.";
                    return null;
                }
                extensionRate = SampleRates[extensionFrequency];
                if (ReadObjectType(data, ref position) != 2)
                {
                    unsupportedReason = "SBR requires a supported AAC-LC core.";
                    return null;
                }
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
            if (objectType == 2 && Remaining(data, position) >= 16)
            {
                if (Read(data, ref position, 11) != 0x2b7 || ReadObjectType(data, ref position) != 5)
                {
                    unsupportedReason = "The AAC configuration extension is not supported.";
                    return null;
                }
                sbr = Read(data, ref position, 1) != 0;
                if (sbr.Value)
                {
                    int extensionFrequency = ReadFrequency(data, ref position);
                    if (extensionFrequency < 0)
                    {
                        unsupportedReason = "The explicit SBR output frequency is not supported.";
                        return null;
                    }
                    extensionRate = SampleRates[extensionFrequency];
                    if (Remaining(data, position) >= 12)
                    {
                        if (Read(data, ref position, 11) != 0x548)
                        {
                            unsupportedReason = "The AAC post-SBR configuration extension is not supported.";
                            return null;
                        }
                        ps = Read(data, ref position, 1) != 0;
                    }
                }
            }
            if (extensionRate is int outputRate && outputRate != SampleRates[frequency] && outputRate != 2 * SampleRates[frequency])
            {
                unsupportedReason = "The SBR output frequency must equal the core frequency or twice that frequency.";
                return null;
            }
            if (Remaining(data, position) > 7)
            {
                unsupportedReason = "The AAC configuration contains additional unsupported data.";
                return null;
            }
            while (Remaining(data, position) > 0)
            {
                if (Read(data, ref position, 1) != 0)
                {
                    throw new InvalidDataException("The AAC configuration padding is not zero.");
                }
            }
            unsupportedReason = null;
            return new(frequency, channels, sbr, extensionRate, ps, data.ToArray());
        }

        private static int ReadObjectType(ReadOnlySpan<byte> data, ref int position)
        {
            int value = Read(data, ref position, 5);
            return value == 31 ? 32 + Read(data, ref position, 6) : value;
        }

        private static int ReadFrequency(ReadOnlySpan<byte> data, ref int position)
        {
            int frequency = Read(data, ref position, 4);
            if (frequency is 13 or 14)
            {
                throw new InvalidDataException("The AAC sampling frequency index is reserved.");
            }
            if (frequency != 15) { return frequency; }
            int rate = Read(data, ref position, 24);
            if (rate == 0) { throw new InvalidDataException("The explicit AAC sampling frequency must be positive."); }
            return SampleRates.IndexOf(rate);
        }

        private static long Remaining(ReadOnlySpan<byte> data, int position) => (long)data.Length * 8 - position;

        private static int Read(ReadOnlySpan<byte> data, ref int position, int count)
        {
            if (count > Remaining(data, position))
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
