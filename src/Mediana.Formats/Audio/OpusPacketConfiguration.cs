// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>RFC 6716 packet coding parameters, independent of output-channel mapping and packet frame packing.</summary>
    public readonly record struct OpusPacketConfiguration
    {
        private readonly byte configurationAndChannels;

        private OpusPacketConfiguration(byte toc) => configurationAndChannels = (byte)(toc >> 2);

        public int Configuration => configurationAndChannels >> 1;
        public int CodedChannelCount => 1 + (configurationAndChannels & 1);

        public OpusCodingMode CodingMode => Configuration switch
        {
            >= 0 and <= 11 => OpusCodingMode.Silk,
            >= 12 and <= 15 => OpusCodingMode.Hybrid,
            >= 16 and <= 31 => OpusCodingMode.Celt,
            _ => throw new InvalidOperationException()
        };

        public OpusBandwidth Bandwidth => Configuration switch
        {
            >= 0 and <= 3 or >= 16 and <= 19 => OpusBandwidth.Narrowband,
            >= 4 and <= 7 => OpusBandwidth.Mediumband,
            >= 8 and <= 11 or >= 20 and <= 23 => OpusBandwidth.Wideband,
            >= 12 and <= 13 or >= 24 and <= 27 => OpusBandwidth.SuperWideband,
            >= 14 and <= 15 or >= 28 and <= 31 => OpusBandwidth.Fullband,
            _ => throw new InvalidOperationException()
        };

        public int SamplesPerFrame => CodingMode switch
        {
            OpusCodingMode.Celt => 120 << (Configuration & 3),
            OpusCodingMode.Hybrid => 480 << (Configuration & 1),
            OpusCodingMode.Silk => (Configuration & 3) switch
            {
                0 => 480,
                1 => 960,
                2 => 1920,
                3 => 2880,
                _ => throw new InvalidOperationException()
            },
            _ => throw new InvalidOperationException()
        };

        public static OpusPacketConfiguration FromToc(byte toc) => new(toc);
    }
}
