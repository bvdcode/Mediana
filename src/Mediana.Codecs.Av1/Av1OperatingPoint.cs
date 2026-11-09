// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1OperatingPoint(int Idc, int Level, bool Tier, uint? DecoderBufferDelay,
        uint? EncoderBufferDelay, bool LowDelay, int? InitialDisplayDelay)
    {
        internal bool IncludesBaseLayer => Idc == 0 || (Idc & 0x101) == 0x101;
    }
}
