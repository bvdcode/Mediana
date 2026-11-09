// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SuperResolution(int UpscaledWidth, int Denominator)
    {
        internal bool Active => Denominator != 0;
        internal int CodedWidth => (UpscaledWidth * 8 + Denominator / 2) / Denominator;
        internal int ScaleDenominator => Active ? Denominator : 8;
        internal Av1SequenceHeader PixelGeometry(Av1SequenceHeader coded) => Active ? coded with { Width = UpscaledWidth } : coded;

        internal static Av1SuperResolution Read(ref Av1BitReader bits, Av1SequenceHeader geometry)
        {
            if (!geometry.SuperResolution || !bits.Flag()) { return default; }
            return new(geometry.Width, bits.Read(3) + 9);
        }
    }
}
