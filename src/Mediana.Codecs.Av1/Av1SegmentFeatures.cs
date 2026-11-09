// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SegmentFeatures(byte Mask, short Quantizer, sbyte Vertical, sbyte Horizontal, sbyte U, sbyte V, byte Reference)
    {
        internal bool ForcedReference => (Mask & 32) != 0;
        internal bool Skip => (Mask & 64) != 0;
        internal bool GlobalMotion => (Mask & 128) != 0;
        internal bool PreSkip => (Mask & 224) != 0;

        internal int QuantizerIndex(int current) => Math.Clamp(current + Quantizer, 0, 255);

        internal int FilterDelta(int component) => component switch
        {
            0 => Vertical,
            1 => Horizontal,
            2 => U,
            3 => V,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };

        internal static Av1SegmentFeatures Read(ref Av1BitReader bits)
        {
            byte mask = 0;
            Span<int> values = stackalloc int[8];
            values.Clear();
            for (int index = 0; index < values.Length; index++)
            {
                if (!bits.Flag()) { continue; }
                mask |= (byte)(1 << index);
                values[index] = index switch
                {
                    0 => Math.Clamp(bits.ReadSigned(9), -255, 255),
                    >= 1 and <= 4 => Math.Clamp(bits.ReadSigned(7), -63, 63),
                    5 => bits.Read(3),
                    6 or 7 => 0,
                    _ => throw new InvalidDataException("The AV1 segment feature is invalid.")
                };
            }
            return new(mask, (short)values[0], (sbyte)values[1], (sbyte)values[2], (sbyte)values[3], (sbyte)values[4], (byte)values[5]);
        }
    }
}
