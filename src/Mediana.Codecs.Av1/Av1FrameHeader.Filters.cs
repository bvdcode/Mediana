// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly partial record struct Av1FrameHeader
    {
        private static void ReadInactiveRestoration(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
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
