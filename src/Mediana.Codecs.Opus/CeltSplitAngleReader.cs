// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Opus
{
    internal static class CeltSplitAngleReader
    {
        public static CeltSplitAngle Read(ref OpusRangeDecoder decoder, CeltSplitMode mode, int budgetEighths,
            int remainingEighths, uint fill)
        {
            Validate(mode);
            int cap = CeltBandTables.WidthLog2Eighths[mode.Band] + mode.FrameShift * 8;
            int offset = (cap >> 1) - 4;
            int degrees = 2 * mode.Dimensions - 1;
            if (mode.Stereo && mode.Dimensions == 2)
            {
                offset = (cap >> 1) - 16;
                degrees--;
            }
            int subdivisions = Subdivisions(budgetEighths, offset, degrees, cap);
            if (mode.Stereo && mode.Band >= mode.IntensityBand) { subdivisions = 1; }
            int before = decoder.EighthBitsUsed;
            int angle = 0;
            bool inverted = false;
            if (subdivisions > 1)
            {
                int symbol;
                if (mode.Stereo && mode.Dimensions > 2) { symbol = decoder.ReadStereoStep(subdivisions); }
                else if (mode.OriginalBlocks > 1 || mode.Stereo) { symbol = (int)decoder.ReadUniform((uint)subdivisions + 1); }
                else { symbol = decoder.ReadTriangular(subdivisions); }
                angle = symbol * 16384 / subdivisions;
            }
            else if (mode.Stereo)
            {
                if (budgetEighths > 16 && remainingEighths > 16) { inverted = decoder.ReadBit(2); }
                if (mode.DisableInversion) { inverted = false; }
            }
            int mid, side, delta;
            uint mask = (1u << mode.Blocks) - 1;
            if (angle == 0)
            {
                mid = 32767;
                side = 0;
                delta = -16384;
                fill &= mask;
            }
            else if (angle == 16384)
            {
                mid = 0;
                side = 32767;
                delta = 16384;
                fill &= mask << mode.Blocks;
            }
            else
            {
                mid = CeltSplitMath.Cosine(angle);
                side = CeltSplitMath.Cosine(16384 - angle);
                delta = CeltSplitMath.Multiply((mode.Dimensions - 1) << 7, CeltSplitMath.Log2Tangent(side, mid));
            }
            return new(angle, mid, side, delta, decoder.EighthBitsUsed - before, inverted, fill);
        }

        private static int Subdivisions(int budget, int offset, int degrees, int cap)
        {
            int bits = Math.Min(64, Math.Min(budget - cap - 32, (budget + degrees * offset) / degrees));
            if (bits < 4) { return 1; }
            int rounded = Powers[bits & 7] >> (14 - (bits >> 3));
            return ((rounded + 1) >> 1) << 1;
        }

        private static void Validate(CeltSplitMode mode)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(mode.Band);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mode.Band, CeltBandTables.BandCount);
            ArgumentOutOfRangeException.ThrowIfLessThan(mode.FrameShift, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(mode.FrameShift, 3);
            ArgumentOutOfRangeException.ThrowIfLessThan(mode.Dimensions, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(mode.Dimensions, 176);
            ValidateBlocks(mode.Blocks);
            ValidateBlocks(mode.OriginalBlocks);
            ArgumentOutOfRangeException.ThrowIfNegative(mode.IntensityBand);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(mode.IntensityBand, CeltBandTables.BandCount);
        }

        private static void ValidateBlocks(int blocks)
        {
            if (blocks is < 1 or > CeltBandTables.MaximumTimeBlocks || !BitOperations.IsPow2(blocks))
            {
                throw new ArgumentOutOfRangeException(nameof(blocks));
            }
        }

        private static readonly ushort[] Powers = [16384, 17866, 19483, 21247, 23170, 25267, 27554, 30048];
    }
}
