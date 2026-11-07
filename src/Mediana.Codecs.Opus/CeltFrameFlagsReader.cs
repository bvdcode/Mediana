// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltFrameFlagsReader
    {
        public static CeltFrameFlags Read(ref OpusRangeDecoder decoder, CeltBandMode mode)
        {
            bool silent = decoder.BitsUsed >= decoder.FrameBitCount;
            if (!silent && decoder.BitsUsed == 1) { silent = decoder.ReadBit(15); }
            if (silent)
            {
                decoder.AccountForSilence();
                return new(true, false, false, default);
            }
            CeltPostFilterParameters postFilter = default;
            if (mode.FirstBand == 0 && decoder.BitsUsed + 16 <= decoder.FrameBitCount && decoder.ReadBit(1))
            {
                int octave = (int)decoder.ReadUniform(6);
                int period = (16 << octave) + (int)decoder.ReadRawBits(4 + octave) - 1;
                float gain = .09375f * (decoder.ReadRawBits(3) + 1);
                int tapset = 0;
                if (decoder.BitsUsed + 2 <= decoder.FrameBitCount) { tapset = decoder.ReadInverseCdf([2, 1, 0], 2); }
                postFilter = new(period, gain, tapset);
            }
            bool transient = false, intra = false;
            if (mode.FrameShift > 0 && decoder.BitsUsed + 3 <= decoder.FrameBitCount) { transient = decoder.ReadBit(3); }
            if (decoder.BitsUsed + 3 <= decoder.FrameBitCount) { intra = decoder.ReadBit(3); }
            return new(false, transient, intra, postFilter);
        }
    }
}
