// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal readonly record struct Ac3TransientEvent(long Transient, int Length)
    {
        public int PreNoiseLength => 256 + (int)(Transient & 255);
        public int CorrectionLength => PreNoiseLength + Length + 256;
        public int SynthesisLength => 512 + PreNoiseLength;
        public long SourceStart => Transient - 512 - 2 * PreNoiseLength;
        public long TargetStart => Transient - CorrectionLength;
    }
}
