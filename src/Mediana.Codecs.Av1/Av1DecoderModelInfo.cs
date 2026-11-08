// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1DecoderModelInfo(int BufferDelayLength, uint NumUnitsInDecodingTick,
        int BufferRemovalTimeLength, int FramePresentationTimeLength)
    {
        internal static Av1DecoderModelInfo Read(ref Av1BitReader bits)
        {
            int delayLength = bits.Read(5) + 1;
            uint units = bits.ReadUnsigned(32);
            if (units == 0)
            {
                throw new InvalidDataException("The AV1 decoding clock tick must be greater than zero.");
            }
            return new(delayLength, units, bits.Read(5) + 1, bits.Read(5) + 1);
        }
    }
}
