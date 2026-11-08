// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1Quantization(int BaseIndex, int YDc, int UDc, int UAc, int VDc, int VAc)
    {
        internal bool Lossless => BaseIndex == 0 && YDc == 0 && UDc == 0 && UAc == 0 && VDc == 0 && VAc == 0;

        internal int Dc(int plane) => Av1QuantizerTables.Dc[Math.Clamp(BaseIndex + (plane switch
        {
            0 => YDc,
            1 => UDc,
            2 => VDc,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        }), 0, 255)];

        internal int Ac(int plane) => Av1QuantizerTables.Ac[Math.Clamp(BaseIndex + (plane switch
        {
            0 => 0,
            1 => UAc,
            2 => VAc,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        }), 0, 255)];
    }
}
