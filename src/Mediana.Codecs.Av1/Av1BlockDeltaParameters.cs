// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1BlockDeltaParameters(bool QuantizerPresent, int QuantizerShift,
        bool FilterPresent, int FilterShift, bool MultiFilter)
    {
        internal static Av1BlockDeltaParameters Read(ref Av1BitReader bits, int quantizer, bool intrabc = false)
        {
            if (quantizer == 0 || !bits.Flag()) { return default; }
            int quantizerShift = bits.Read(2);
            bool filter = !intrabc && bits.Flag();
            int filterShift = filter ? bits.Read(2) : 0;
            bool multi = filter && bits.Flag();
            return new(true, quantizerShift, filter, filterShift, multi);
        }
    }
}
