// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Fixed and variable ADTS header fields, excluding optional CRC words and raw data.</summary>
    public readonly record struct AdtsFrameHeader
    {
        public const int ByteLength = 7;
        public const int MaximumFrameBytes = 8191;

        private AdtsFrameHeader(bool mpeg2, bool hasCrc, int objectType, int frequencyIndex,
            int channels, int frameBytes, int rawDataBlocks)
        {
            IsMpeg2 = mpeg2;
            HasCrc = hasCrc;
            AudioObjectType = objectType;
            FrequencyIndex = frequencyIndex;
            ChannelConfiguration = channels;
            FrameBytes = frameBytes;
            RawDataBlockCount = rawDataBlocks;
        }

        public bool IsMpeg2 { get; }
        public bool HasCrc { get; }
        public int AudioObjectType { get; }
        public int FrequencyIndex { get; }
        /// <summary>Core sampling frequency. Implicit SBR can produce PCM at twice this rate.</summary>
        public int SampleRate => AacConfiguration.SampleRates[FrequencyIndex];
        public int ChannelConfiguration { get; }
        public int FrameBytes { get; }
        public int RawDataBlockCount { get; }
        /// <summary>Includes the header CRC and, for multiple protected blocks, their position table.</summary>
        public int HeaderBytes => ByteLength + (HasCrc ? 2 * RawDataBlockCount : 0);

        public static bool HasSync(ReadOnlySpan<byte> data) => data.Length >= 2
            && data[0] == 0xff && (data[1] & 0xf6) == 0xf0;

        public static AdtsFrameHeader Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < ByteLength || !HasSync(data))
            {
                throw new InvalidDataException("The ADTS header is truncated or has an invalid sync word or layer.");
            }
            int frequency = (data[2] >> 2) & 15;
            if (frequency >= AacConfiguration.SampleRates.Length)
            {
                throw new InvalidDataException("The ADTS sampling frequency index is reserved.");
            }
            bool mpeg2 = (data[1] & 8) != 0;
            int objectType = (data[2] >> 6) + 1;
            if (mpeg2 && objectType == 4)
            {
                throw new InvalidDataException("The MPEG-2 ADTS profile is reserved.");
            }
            AdtsFrameHeader result = new(mpeg2, (data[1] & 1) == 0, objectType, frequency,
                ((data[2] & 1) << 2) | (data[3] >> 6),
                ((data[3] & 3) << 11) | (data[4] << 3) | (data[5] >> 5), (data[6] & 3) + 1);
            int minimumPayload = result.RawDataBlockCount + (result.HasCrc && result.RawDataBlockCount > 1
                ? 2 * result.RawDataBlockCount : 0);
            if (result.FrameBytes < result.HeaderBytes + minimumPayload)
            {
                throw new InvalidDataException("The ADTS frame length cannot contain its headers and raw data blocks.");
            }
            return result;
        }

        /// <summary>Writes the core GASpecificConfig; in-band SBR and program configurations remain in raw data.</summary>
        public byte[] ToAudioSpecificConfig()
        {
            int value = (AudioObjectType << 11) | (FrequencyIndex << 7) | (ChannelConfiguration << 3);
            return [(byte)(value >> 8), (byte)value];
        }
    }
}
