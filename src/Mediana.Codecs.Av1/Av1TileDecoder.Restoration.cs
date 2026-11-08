// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void ReadRestoration(int row, int column, int side)
        {
            Av1RestorationParameters parameters = header.Restoration;
            for (int plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
            {
                Av1RestorationType type = parameters.Type(plane);
                if (type == Av1RestorationType.None) { continue; }
                int size = parameters.Size(plane);
                int step = 4 >> sequence.PlaneShift(plane);
                int columns = parameters.Columns(header.PixelGeometry, plane);
                int startRow = (row * step + size - 1) / size;
                int endRow = Math.Min(parameters.Rows(sequence, plane), ((row + side) * step + size - 1) / size);
                int numerator = step * header.SuperResolution.ScaleDenominator;
                int denominator = size * 8;
                int startColumn = (column * numerator + denominator - 1) / denominator;
                int endColumn = Math.Min(columns, ((column + side) * numerator + denominator - 1) / denominator);
                int offset = parameters.Offset(header.PixelGeometry, plane);
                for (int y = startRow; y < endRow; y++)
                {
                    for (int x = startColumn; x < endColumn; x++)
                    {
                        restorationUnits[offset + y * columns + x] = ReadRestorationUnit(type, plane);
                    }
                }
            }
        }

        private Av1RestorationUnit ReadRestorationUnit(Av1RestorationType type, int plane)
        {
            type = type switch
            {
                Av1RestorationType.Wiener => symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.WienerOffset, 3)) != 0 ? Av1RestorationType.Wiener : Av1RestorationType.None,
                Av1RestorationType.SelfGuided => symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SelfGuidedOffset, 3)) != 0 ? Av1RestorationType.SelfGuided : Av1RestorationType.None,
                Av1RestorationType.Switchable => (Av1RestorationType)symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.RestorationOffset, 4)),
                Av1RestorationType.None => throw new InvalidOperationException("An inactive AV1 restoration plane has no unit syntax."),
                _ => throw new InvalidDataException("The AV1 restoration type is invalid.")
            };
            Av1RestorationUnit unit = new() { Type = type };
            ref Av1RestorationUnit reference = ref restorationReferences[plane];
            switch (type)
            {
                case Av1RestorationType.Wiener:
                    unit.V0 = plane == 0 ? (sbyte)ReadRestorationSubexp(-5, 11, 1, reference.V0) : (sbyte)0;
                    unit.V1 = (sbyte)ReadRestorationSubexp(-23, 9, 2, reference.V1);
                    unit.V2 = (sbyte)ReadRestorationSubexp(-17, 47, 3, reference.V2);
                    unit.H0 = plane == 0 ? (sbyte)ReadRestorationSubexp(-5, 11, 1, reference.H0) : (sbyte)0;
                    unit.H1 = (sbyte)ReadRestorationSubexp(-23, 9, 2, reference.H1);
                    unit.H2 = (sbyte)ReadRestorationSubexp(-17, 47, 3, reference.H2);
                    reference.V0 = unit.V0; reference.V1 = unit.V1; reference.V2 = unit.V2;
                    reference.H0 = unit.H0; reference.H1 = unit.H1; reference.H2 = unit.H2;
                    break;
                case Av1RestorationType.SelfGuided:
                    unit.Set = (byte)symbols.ReadLiteral(4);
                    unit.X0 = Av1SelfGuidedParameters.Radius(unit.Set, 0) != 0
                        ? (sbyte)ReadRestorationSubexp(-96, 32, 4, reference.X0) : (sbyte)0;
                    reference.X0 = unit.X0;
                    unit.X1 = Av1SelfGuidedParameters.Radius(unit.Set, 1) != 0
                        ? (sbyte)ReadRestorationSubexp(-32, 96, 4, reference.X1) : (sbyte)Math.Clamp(128 - reference.X0, -32, 95);
                    reference.X1 = unit.X1;
                    break;
                case Av1RestorationType.None:
                    break;
                case Av1RestorationType.Switchable:
                default:
                    throw new InvalidDataException("The AV1 restoration unit type is invalid.");
            }
            return unit;
        }

        private int ReadRestorationSubexp(int low, int high, int k, int reference)
        {
            int count = high - low;
            int index = 0;
            int accumulated = 0;
            int value;
            while (true)
            {
                int width = index != 0 ? k + index - 1 : k;
                int step = 1 << width;
                if (count <= accumulated + 3 * step) { value = symbols.ReadNs(count - accumulated) + accumulated; break; }
                if (!symbols.ReadBoolean()) { value = (int)symbols.ReadLiteral(width) + accumulated; break; }
                index++;
                accumulated += step;
            }
            int centered = reference - low;
            if ((centered << 1) <= count) { return low + Av1Subexponential.InverseRecenter(centered, value); }
            return high - 1 - Av1Subexponential.InverseRecenter(count - 1 - centered, value);
        }
    }
}
