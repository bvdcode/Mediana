// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal struct CeltBandDecoderState(CeltPulseCodebook codebook, int band, CeltSpreading spreading,
        int remainingEighths, uint seed, int intensityBand = CeltBandTables.BandCount, bool disableInversion = false)
    {
        public CeltPulseCodebook Codebook { get; } = codebook ?? throw new ArgumentNullException(nameof(codebook));
        public int Band { get; set; } = band;
        public CeltSpreading Spreading { get; set; } = spreading;
        public int RemainingEighths { get; set; } = remainingEighths;
        public uint Seed { get; set; } = seed;
        public int IntensityBand { get; set; } = intensityBand;
        public bool DisableInversion { get; set; } = disableInversion;
    }
}
