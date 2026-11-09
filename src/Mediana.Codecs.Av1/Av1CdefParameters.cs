// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1CdefParameters(bool Present, int Damping, int IndexBits, Av1CdefStrengths Strengths)
    {
        internal bool Enabled
        {
            get
            {
                for (int index = 0; index < 1 << IndexBits; index++)
                {
                    if (Strengths[index] != 0) { return true; }
                }
                return false;
            }
        }

        internal static Av1CdefParameters Read(ref Av1BitReader bits, bool monochrome)
        {
            int damping = bits.Read(2) + 3;
            int indexBits = bits.Read(2);
            Av1CdefStrengths strengths = default;
            for (int index = 0; index < 1 << indexBits; index++)
            {
                int primary = bits.Read(4);
                int secondary = bits.Read(2);
                if (secondary == 3) { secondary = 4; }
                int packed = primary | (secondary << 4);
                if (!monochrome)
                {
                    primary = bits.Read(4);
                    secondary = bits.Read(2);
                    if (secondary == 3) { secondary = 4; }
                    packed |= (primary << 7) | (secondary << 11);
                }
                strengths[index] = (ushort)packed;
            }
            return new(true, damping, indexBits, strengths);
        }

        internal int Primary(int index, int plane) => (Strengths[index] >> (plane == 0 ? 0 : 7)) & 15;
        internal int Secondary(int index, int plane) => (Strengths[index] >> (plane == 0 ? 4 : 11)) & 7;
        internal int IndexCount(Av1SequenceHeader sequence) => Present && IndexBits != 0
            ? checked(((sequence.MiColumns + 15) >> 4) * ((sequence.MiRows + 15) >> 4)) : 0;
    }
}
