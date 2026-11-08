// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1Quantization(int BaseIndex, int YDc, int UDc, int UAc, int VDc, int VAc, Av1QuantizationMatrix Matrix = default)
    {
        internal bool Lossless => BaseIndex == 0 && YDc == 0 && UDc == 0 && UAc == 0 && VDc == 0 && VAc == 0;

        internal int Dc(int plane, int bitDepth = 8) => Av1QuantizerTables.DcForDepth(bitDepth)[Math.Clamp(BaseIndex + (plane switch
        {
            0 => YDc,
            1 => UDc,
            2 => VDc,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        }), 0, 255)];

        internal int Ac(int plane, int bitDepth = 8) => Av1QuantizerTables.AcForDepth(bitDepth)[Math.Clamp(BaseIndex + (plane switch
        {
            0 => 0,
            1 => UAc,
            2 => VAc,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        }), 0, 255)];

        internal static int Dequantize(int level, int multiplier, bool negative, int shift, int bitDepth = 8)
        {
            int value = (int)(((long)level * multiplier) & 0xFFFFFF) >> shift;
            int precision = bitDepth switch
            {
                8 => 15,
                10 => 17,
                12 => 19,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            int maximum = (1 << precision) - 1;
            return Math.Clamp(negative ? -value : value, -maximum - 1, maximum);
        }
    }
}
