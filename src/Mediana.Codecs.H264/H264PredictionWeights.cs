// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264PredictionWeights
    {
        public static bool IsIdentity(this H264WeightTable table, int reference)
        {
            H264PredictionWeight entry = table.Entries[reference];
            int chroma = 1 << table.ChromaDenominator;
            return entry.LumaWeight == 1 << table.LumaDenominator && entry.CbWeight == chroma && entry.CrWeight == chroma
                && (entry.LumaOffset | entry.CbOffset | entry.CrOffset) == 0;
        }

        public static void Apply(this H264WeightTable table, int reference, int plane, Span<byte> samples, int bitDepth = 8)
        {
            (int weight, int offset, int denominator) = table.Get(reference, plane);
            if (bitDepth == 8 && offset == 0 && weight == 1 << denominator)
            {
                return;
            }
            int rounding = denominator == 0 ? 0 : 1 << (denominator - 1);
            if (bitDepth > 8)
            {
                Span<ushort> pixels = MemoryMarshal.Cast<byte, ushort>(samples);
                int maximum = (1 << bitDepth) - 1;
                int scaledOffset = offset << (bitDepth - 8);
                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = (ushort)Math.Clamp(((weight * pixels[index] + rounding) >> denominator) + scaledOffset, 0, maximum);
                }
                return;
            }
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (byte)Math.Clamp(((weight * samples[index] + rounding) >> denominator) + offset, 0, 255);
            }
        }

        public static (int Weight, int Offset, int Denominator) Get(this H264WeightTable table, int reference, int plane)
        {
            H264PredictionWeight entry = table.Entries[reference];
            return plane switch
            {
                0 => (entry.LumaWeight, entry.LumaOffset, table.LumaDenominator),
                1 => (entry.CbWeight, entry.CbOffset, table.ChromaDenominator),
                2 => (entry.CrWeight, entry.CrOffset, table.ChromaDenominator),
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
        }
    }
}
