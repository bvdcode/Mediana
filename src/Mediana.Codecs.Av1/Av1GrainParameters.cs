// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal record Av1GrainParameters(ushort Seed, ReadOnlyMemory<Av1GrainPoint> LumaPoints,
        ReadOnlyMemory<sbyte> LumaCoefficients, Av1GrainChannel U, Av1GrainChannel V,
        bool ChromaScalingFromLuma, int ScalingShift, int Lag, int CoefficientShift,
        int GrainShift, bool Overlap, bool RestrictedRange)
    {
        internal static Av1GrainParameters ReadUpdate(ref Av1BitReader bits, ushort seed, bool monochrome)
        {
            Av1GrainPoint[] luma = ReadPoints(ref bits, 14);
            bool fromLuma = !monochrome && bits.Flag();
            Av1GrainPoint[] u = [];
            Av1GrainPoint[] v = [];
            if (!monochrome && !fromLuma && luma.Length != 0)
            {
                u = ReadPoints(ref bits, 10);
                v = ReadPoints(ref bits, 10);
                if ((u.Length == 0) != (v.Length == 0))
                {
                    throw new InvalidDataException("AV1 4:2:0 film grain must apply to both chroma planes or neither.");
                }
            }
            int scalingShift = bits.Read(2) + 8;
            int lag = bits.Read(2);
            int lumaCount = 2 * lag * (lag + 1);
            int chromaCount = lumaCount + (luma.Length == 0 ? 0 : 1);
            sbyte[] yCoefficients = luma.Length == 0 ? [] : ReadCoefficients(ref bits, lumaCount);
            sbyte[] uCoefficients = fromLuma || u.Length != 0 ? ReadCoefficients(ref bits, chromaCount) : [];
            sbyte[] vCoefficients = fromLuma || v.Length != 0 ? ReadCoefficients(ref bits, chromaCount) : [];
            int coefficientShift = bits.Read(2) + 6;
            int grainShift = bits.Read(2);
            Av1GrainChannel cb = ReadChannel(ref bits, u, uCoefficients);
            Av1GrainChannel cr = ReadChannel(ref bits, v, vCoefficients);
            return new(seed, luma, yCoefficients, cb, cr, fromLuma, scalingShift, lag,
                coefficientShift, grainShift, bits.Flag(), bits.Flag());
        }

        private static Av1GrainChannel ReadChannel(ref Av1BitReader bits, Av1GrainPoint[] points, sbyte[] coefficients)
        {
            if (points.Length == 0) { return new(points, coefficients, 0, 0, 0); }
            int multiplier = bits.Read(8) - 128;
            int lumaMultiplier = bits.Read(8) - 128;
            int offset = bits.Read(9) - 256;
            return new(points, coefficients, multiplier, lumaMultiplier, offset);
        }

        private static Av1GrainPoint[] ReadPoints(ref Av1BitReader bits, int maximum)
        {
            int count = bits.Read(4);
            if (count > maximum) { throw new InvalidDataException("The AV1 film-grain scaling-point count is invalid."); }
            Av1GrainPoint[] points = count == 0 ? [] : new Av1GrainPoint[count];
            int previous = -1;
            for (int index = 0; index < count; index++)
            {
                byte value = (byte)bits.Read(8);
                byte scaling = (byte)bits.Read(8);
                if (value <= previous) { throw new InvalidDataException("AV1 film-grain scaling points must strictly increase."); }
                points[index] = new(value, scaling);
                previous = value;
            }
            return points;
        }

        private static sbyte[] ReadCoefficients(ref Av1BitReader bits, int count)
        {
            sbyte[] values = count == 0 ? [] : new sbyte[count];
            for (int index = 0; index < count; index++) { values[index] = (sbyte)(bits.Read(8) - 128); }
            return values;
        }
    }
}
