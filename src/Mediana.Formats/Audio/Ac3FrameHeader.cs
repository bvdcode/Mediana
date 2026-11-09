// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Self-describing AC-3 or E-AC-3 syncframe header, as specified by ETSI TS 102 366.</summary>
    public class Ac3FrameHeader
    {
        public const string CodecId = "ac3";
        public const string EnhancedCodecId = "eac3";
        public const int HeaderBytes = 8;
        private static ReadOnlySpan<int> BitRates => [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640];
        private static ReadOnlySpan<int> Words44100 => [69, 87, 104, 121, 139, 174, 208, 243, 278, 348, 417, 487, 557, 696, 835, 975, 1114, 1253, 1393];

        private Ac3FrameHeader(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < HeaderBytes || bytes[0] != 0x0b || bytes[1] != 0x77)
            {
                throw new InvalidDataException("An AC-3 syncframe requires a valid eight-byte header.");
            }
            BitstreamId = bytes[5] >> 3;
            IsEnhanced = BitstreamId > 10;
            if (IsEnhanced)
            {
                if (BitstreamId > 16) { throw new NotSupportedException("The enhanced AC-3 bitstream version is not supported."); }
                StreamType = bytes[2] >> 6;
                if (StreamType == 3) { throw new InvalidDataException("The E-AC-3 stream type is reserved."); }
                SubstreamId = bytes[2] >> 3 & 7;
                FrameBytes = (((bytes[2] & 7) << 8 | bytes[3]) + 1) * 2;
                SampleRateCode = bytes[4] >> 6;
                int code = bytes[4] >> 4 & 3;
                if (SampleRateCode == 3)
                {
                    throw new InvalidDataException("The E-AC-3 sample rate code is reserved.");
                }
                SampleRate = Ac3ChannelLayout.GetSampleRate(SampleRateCode);
                SamplesPerFrame = code switch { 0 => 256, 1 => 512, 2 => 768, 3 => 1536, _ => throw new InvalidOperationException() };
                ChannelMode = bytes[4] >> 1 & 7;
                HasLfe = (bytes[4] & 1) != 0;
            }
            else
            {
                if (BitstreamId > 8) { throw new NotSupportedException("AC-3 bitstream versions above eight are not supported."); }
                SampleRateCode = bytes[4] >> 6;
                int sizeCode = bytes[4] & 63;
                if (SampleRateCode == 3 || sizeCode >= 38) { throw new InvalidDataException("The AC-3 sample rate or frame size code is reserved."); }
                SampleRate = Ac3ChannelLayout.GetSampleRate(SampleRateCode);
                int rate = BitRates[sizeCode / 2];
                FrameBytes = SampleRateCode switch
                {
                    0 => rate * 4,
                    1 => (Words44100[sizeCode / 2] + (sizeCode & 1)) * 2,
                    2 => rate * 6,
                    _ => throw new InvalidOperationException()
                };
                SamplesPerFrame = 1536;
                ChannelMode = bytes[6] >> 5;
                int bit = 51;
                if ((ChannelMode & 1) != 0 && ChannelMode != 1) { bit += 2; }
                if ((ChannelMode & 4) != 0) { bit += 2; }
                if (ChannelMode == 2) { bit += 2; }
                HasLfe = (bytes[bit / 8] >> (7 - bit % 8) & 1) != 0;
            }
            ChannelCount = Ac3ChannelLayout.GetChannelCount(ChannelMode, HasLfe);
            if (FrameBytes < HeaderBytes + 2) { throw new InvalidDataException("The AC-3 syncframe is too short for its header and checksum."); }
        }

        public bool IsEnhanced { get; }
        public int BitstreamId { get; }
        public int StreamType { get; }
        public int SubstreamId { get; }
        public int SampleRateCode { get; }
        public int SampleRate { get; }
        public int FrameBytes { get; }
        public int SamplesPerFrame { get; }
        public int ChannelMode { get; }
        public int ChannelCount { get; }
        public bool HasLfe { get; }
        /// <summary>Channels in coded order; decoding preserves this order without downmixing.</summary>
        public string[] GetChannelOrder() => Ac3ChannelLayout.GetChannelOrder(ChannelMode, HasLfe);
        public static Ac3FrameHeader Read(ReadOnlySpan<byte> data) => new(data);
    }
}
