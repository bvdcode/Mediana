// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly partial record struct Av1FrameHeader
    {
        private static void ReadInactiveFilters(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
            int horizontal = bits.Read(6);
            int vertical = bits.Read(6);
            if (horizontal != 0 || vertical != 0)
            {
                throw new NotSupportedException("AV1 deblocking reconstruction is not implemented.");
            }
            bits.Read(3);
            if (bits.Flag() && bits.Flag())
            {
                for (int index = 0; index < 10; index++)
                {
                    if (bits.Flag())
                    {
                        bits.ReadSigned(7);
                    }
                }
            }
            if (sequence.Cdef)
            {
                bits.Read(2);
                int strengthBits = bits.Read(2);
                if (strengthBits != 0)
                {
                    throw new NotSupportedException("AV1 directional enhancement reconstruction is not implemented.");
                }
                int strength = bits.Read(6);
                if (!sequence.Monochrome)
                {
                    strength |= bits.Read(6);
                }
                if (strength != 0)
                {
                    throw new NotSupportedException("AV1 directional enhancement reconstruction is not implemented.");
                }
            }
            if (sequence.Restoration)
            {
                int planes = sequence.Monochrome ? 1 : 3;
                for (int plane = 0; plane < planes; plane++)
                {
                    if (bits.Read(2) != 0)
                    {
                        throw new NotSupportedException("AV1 loop restoration reconstruction is not implemented.");
                    }
                }
            }
        }
    }
}
