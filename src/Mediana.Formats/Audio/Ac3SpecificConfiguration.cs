// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Three-byte dac3 payload, specified by ETSI TS 102 366 Annex F.4.</summary>
    public class Ac3SpecificConfiguration
    {
        private Ac3SpecificConfiguration(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 3) { throw new InvalidDataException("An AC-3 specific configuration requires three bytes."); }
            int value = bytes[0] << 16 | bytes[1] << 8 | bytes[2];
            SampleRate = Ac3ChannelLayout.GetSampleRate(value >> 22);
            BitstreamId = value >> 17 & 31; BitstreamMode = value >> 14 & 7;
            ChannelMode = value >> 11 & 7; HasLfe = (value & 1 << 10) != 0;
            BitRateCode = value >> 5 & 31;
            if ((value & 31) != 0 || BitRateCode > 18 || BitstreamId > 10)
            { throw new InvalidDataException("The AC-3 specific configuration contains reserved fields."); }
            if (BitstreamId > 8) { throw new NotSupportedException("AC-3 bitstream versions above eight are not supported."); }
        }

        public int SampleRate { get; }
        public int BitstreamId { get; }
        public int BitstreamMode { get; }
        public int ChannelMode { get; }
        public bool HasLfe { get; }
        public int BitRateCode { get; }
        public int ChannelCount => Ac3ChannelLayout.GetChannelCount(ChannelMode, HasLfe);
        public string[] GetChannelOrder() => Ac3ChannelLayout.GetChannelOrder(ChannelMode, HasLfe);
        public static Ac3SpecificConfiguration Read(ReadOnlySpan<byte> data) => new(data);
    }
}
