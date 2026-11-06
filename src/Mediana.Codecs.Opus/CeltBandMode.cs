// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
    }
}
