// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal struct CeltBandDecoderState(CeltPulseCodebook codebook, int band, CeltSpreading spreading,
        int remainingEighths, uint seed)
    {
        public CeltPulseCodebook Codebook { get; } = codebook ?? throw new ArgumentNullException(nameof(codebook));
        public int Band { get; set; } = band;
        public CeltSpreading Spreading { get; set; } = spreading;
        public int RemainingEighths { get; set; } = remainingEighths;
        public uint Seed { get; set; } = seed;
    }
}
