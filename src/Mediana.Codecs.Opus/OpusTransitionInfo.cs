// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal readonly record struct OpusTransitionInfo(bool AtStart, int Bytes)
    {
        public static OpusTransitionInfo Read(ref OpusRangeDecoder range, bool hybrid)
        {
            int required = 17;
            if (hybrid) { required += 20; }
            if (range.BitsUsed + required > range.FrameBitCount) { return default; }
            if (hybrid && !range.ReadBit(12)) { return default; }
            bool atStart = range.ReadBit(1);
            int originalBytes = range.FrameBitCount / 8;
            int bytes;
            if (hybrid) { bytes = (int)range.ReadUniform(256) + 2; }
            else { bytes = originalBytes - (range.BitsUsed + 7) / 8; }
            int remaining = originalBytes - bytes;
            if (bytes < 2 || remaining * 8 < range.BitsUsed)
            {
                throw new InvalidDataException("The Opus transition redundancy exceeds its frame.");
            }
            range.LimitFrameLength(remaining);
            return new(atStart, bytes);
        }
    }
}
