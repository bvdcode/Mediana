// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct SilkFrameMode
    {
        public SilkFrameMode(int sampleRate, int subframes)
        {
            if (sampleRate is not (8000 or 12000 or 16000)) { throw new ArgumentOutOfRangeException(nameof(sampleRate)); }
            if (subframes is not (2 or 4)) { throw new ArgumentOutOfRangeException(nameof(subframes)); }
            SampleRate = sampleRate; Subframes = subframes;
        }

        public int SampleRate { get; }
        public int Subframes { get; }
        public int SamplesPerSubframe => 5 * RateKhz;
        public int FrameSamples => SamplesPerSubframe * Subframes;
        public int LpcOrder => RateKhz switch
        {
            8 or 12 => 10,
            16 => 16,
            _ => throw new InvalidOperationException()
        };

        public int RateKhz => SampleRate switch
        {
            8000 => 8,
            12000 => 12,
            16000 => 16,
            _ => throw new InvalidOperationException("The SILK frame mode is uninitialized.")
        };
    }
}
