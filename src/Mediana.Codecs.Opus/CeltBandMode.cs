// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal readonly record struct CeltBandMode
    {
        public CeltBandMode(int channels, int frameShift, int firstBand, int endBand)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
            ArgumentOutOfRangeException.ThrowIfNegative(frameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            ArgumentOutOfRangeException.ThrowIfNegative(firstBand);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endBand, firstBand);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(endBand, CeltBandTables.BandCount);
            Channels = channels;
            FrameShift = frameShift;
            FirstBand = firstBand;
            EndBand = endBand;
        }

        public int Channels { get; }
        public int FrameShift { get; }
        public int FirstBand { get; }
        public int EndBand { get; }

        public static CeltBandMode FromConfiguration(OpusPacketConfiguration configuration)
        {
            if (configuration.CodingMode != OpusCodingMode.Celt)
            {
                throw new ArgumentException("The Opus packet must select CELT-only coding.", nameof(configuration));
            }
            int end = configuration.Bandwidth switch
            {
                OpusBandwidth.Narrowband => 13,
                OpusBandwidth.Wideband => 17,
                OpusBandwidth.SuperWideband => 19,
                OpusBandwidth.Fullband => CeltBandTables.BandCount,
                OpusBandwidth.Mediumband => throw new InvalidDataException("CELT has no mediumband configuration."),
                _ => throw new ArgumentOutOfRangeException(nameof(configuration))
            };
            return new(configuration.CodedChannelCount, configuration.Configuration & 3, 0, end);
        }
    }
}
