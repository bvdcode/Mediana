// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1TimingInfo(uint NumUnitsInDisplayTick, uint TimeScale, uint? NumTicksPerPictureMinusOne)
    {
        internal bool EqualPictureInterval => NumTicksPerPictureMinusOne.HasValue;

        internal FrameRate? FrameRate
        {
            get
            {
                if (NumTicksPerPictureMinusOne is not uint ticks) { return null; }
                ulong denominator = (ulong)NumUnitsInDisplayTick * ((ulong)ticks + 1);
                return new(TimeScale, denominator);
            }
        }

        internal static Av1TimingInfo Read(ref Av1BitReader bits)
        {
            uint units = bits.ReadUnsigned(32);
            uint scale = bits.ReadUnsigned(32);
            if (units == 0 || scale == 0)
            {
                throw new InvalidDataException("AV1 timing clock values must be greater than zero.");
            }
            uint? ticks = null;
            if (bits.Flag())
            {
                ticks = bits.ReadUvlc();
                if (ticks == uint.MaxValue)
                {
                    throw new InvalidDataException("The AV1 picture interval exceeds its permitted range.");
                }
            }
            return new(units, scale, ticks);
        }
    }
}
