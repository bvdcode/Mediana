// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcPictureOrderCounter
    {
        private readonly int modulus;
        private int previousLsb;
        private long previousMsb;
        private bool hasPrevious;

        public HevcPictureOrderCounter(int log2PictureOrderCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(log2PictureOrderCount, 4);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(log2PictureOrderCount, 16);
            modulus = 1 << log2PictureOrderCount;
        }

        public int Derive(int nalType, int temporalId, int lsb, bool noRaslOutput)
        {
            if (nalType is not (>= 0 and <= 9 or >= 16 and <= 21) || temporalId is < 0 or > 6 ||
                lsb < 0 || lsb >= modulus)
            {
                throw new InvalidDataException("Invalid HEVC picture order inputs.");
            }
            bool randomAccess = nalType is >= 16 and <= 21;
            if ((randomAccess && temporalId != 0) || (!randomAccess && noRaslOutput) ||
                (nalType is >= 16 and <= 20 && !noRaslOutput) || (nalType is 19 or 20 && lsb != 0))
            {
                throw new InvalidDataException("The HEVC random-access picture order declaration is inconsistent.");
            }
            long msb;
            if (randomAccess && noRaslOutput)
            {
                msb = 0;
            }
            else
            {
                if (!hasPrevious)
                {
                    throw new InvalidDataException("HEVC picture order requires an initial random-access picture.");
                }
                msb = previousMsb;
                if (lsb < previousLsb && previousLsb - lsb >= modulus / 2) { msb += modulus; }
                else if (lsb > previousLsb && lsb - previousLsb > modulus / 2) { msb -= modulus; }
            }
            long order = msb + lsb;
            if (order < int.MinValue || order > int.MaxValue)
            {
                throw new InvalidDataException("HEVC picture order exceeds its signed 32-bit range.");
            }
            if (temporalId == 0 && nalType is not (0 or 2 or 4 or >= 6 and <= 9))
            {
                previousLsb = lsb;
                previousMsb = msb;
                hasPrevious = true;
            }
            return (int)order;
        }

        public void Reset()
        {
            previousLsb = 0;
            previousMsb = 0;
            hasPrevious = false;
        }
    }
}
