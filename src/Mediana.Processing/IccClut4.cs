// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;

namespace Mediana.Processing
{
    internal class IccClut4
    {
        private readonly Vector3[] values;
        private readonly int[] strides = new int[4];
        private readonly IccClutCoordinate[][] coordinates = new IccClutCoordinate[4][];

        internal IccClut4(ReadOnlyMemory<byte> encoded, ReadOnlySpan<byte> grid, int precision,
            IccToneCurve[] inputs, CancellationToken token)
        {
            if (grid.Length != 4 || inputs.Length != 4 || precision is not (1 or 2))
            { throw new InvalidDataException("ICC CMYK CLUT requires four inputs and eight- or sixteen-bit samples."); }
            long cells = 1;
            for (int i = 3; i >= 0; i--)
            {
                if (grid[i] < 2) { throw new InvalidDataException("ICC CLUT dimensions require at least two grid points."); }
                if (cells > int.MaxValue) { throw new InvalidDataException("ICC CLUT dimensions exceed its storage."); }
                strides[i] = (int)cells; cells *= grid[i];
            }
            if (cells * 3 * precision != encoded.Length) { throw new InvalidDataException("ICC CLUT sample extent is invalid."); }
            values = new Vector3[(int)cells];
            ReadOnlySpan<byte> bytes = encoded.Span;
            for (int i = 0; i < values.Length; i++)
            {
                if ((i & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                int offset = i * 3 * precision;
                values[i] = precision switch
                {
                    1 => new Vector3(bytes[offset], bytes[offset + 1], bytes[offset + 2]) / 255,
                    2 => new Vector3(BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 4)..])) / 65535,
                    _ => throw new InvalidOperationException("Invalid ICC CLUT precision.")
                };
            }
            for (int channel = 0; channel < 4; channel++)
            {
                coordinates[channel] = new IccClutCoordinate[256];
                for (int sample = 0; sample < 256; sample++)
                {
                    token.ThrowIfCancellationRequested();
                    double value = inputs[channel].Evaluate(sample / 255.0) * (grid[channel] - 1);
                    int cell = Math.Min((int)value, grid[channel] - 2);
                    coordinates[channel][sample] = new(cell * strides[channel], (float)(value - cell));
                }
            }
        }

        internal Vector3 Evaluate(ReadOnlySpan<byte> source)
        {
            IccClutCoordinate c = coordinates[0][source[0]], m = coordinates[1][source[1]],
                y = coordinates[2][source[2]], k = coordinates[3][source[3]];
            int start = c.Offset + m.Offset + y.Offset + k.Offset;
            float first = m.Fraction, second = y.Fraction, third = k.Fraction;
            int a = strides[1], b = strides[2], d = strides[3];
            if (first < second) { (first, second) = (second, first); (a, b) = (b, a); }
            if (second < third) { (second, third) = (third, second); (b, d) = (d, b); }
            if (first < second) { (first, second) = (second, first); (a, b) = (b, a); }
            Vector3 low = Tetrahedron(start, a, b, d, first, second, third);
            Vector3 high = Tetrahedron(start + strides[0], a, b, d, first, second, third);
            return low + (high - low) * c.Fraction;
        }

        private Vector3 Tetrahedron(int start, int a, int b, int c, float first, float second, float third)
        {
            Vector3 v0 = values[start], v1 = values[start + a], v2 = values[start + a + b], v3 = values[start + a + b + c];
            return v0 + (v1 - v0) * first + (v2 - v1) * second + (v3 - v2) * third;
        }
    }
}
