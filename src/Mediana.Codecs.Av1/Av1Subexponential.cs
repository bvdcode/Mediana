// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1Subexponential
    {
        internal static int ReadSigned(ref Av1BitReader bits, int low, int high, int reference)
        {
            if (reference < low || reference >= high) { throw new ArgumentOutOfRangeException(nameof(reference)); }
            int count = checked(high - low);
            int index = 0;
            int accumulated = 0;
            int value;
            while (true)
            {
                int width = index == 0 ? 3 : 3 + index - 1;
                int step = 1 << width;
                if (count <= accumulated + 3 * step) { value = accumulated + bits.ReadNs(count - accumulated); break; }
                if (!bits.Flag()) { value = accumulated + bits.Read(width); break; }
                index++;
                accumulated += step;
            }
            int centered = reference - low;
            if ((centered << 1) <= count) { return low + InverseRecenter(centered, value); }
            return high - 1 - InverseRecenter(count - 1 - centered, value);
        }

        internal static int InverseRecenter(int reference, int value) => value > 2 * reference ? value
            : (value & 1) != 0 ? reference - ((value + 1) >> 1) : reference + (value >> 1);
    }
}
