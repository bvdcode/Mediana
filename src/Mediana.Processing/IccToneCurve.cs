// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Processing
{
    internal class IccToneCurve
    {
        private readonly ReadOnlyMemory<byte> table;
        private readonly int precision;
        private readonly int count;
        private readonly double gamma = 1;
        private readonly double[]? parameters;
        private readonly int function;
        internal int EncodedLength { get; }

        internal IccToneCurve(ReadOnlyMemory<byte> data)
        {
            ReadOnlySpan<byte> tag = data.Span;
            if (tag.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(tag[4..]) != 0)
            { throw new InvalidDataException("ICC tone curve header is invalid."); }
            if (tag.StartsWith("curv"u8))
            {
                uint entries = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
                if (entries > (tag.Length - 12) / 2) { throw new InvalidDataException("ICC sampled tone curve is truncated."); }
                EncodedLength = 12 + (int)entries * 2;
                if (entries == 1)
                {
                    gamma = BinaryPrimitives.ReadUInt16BigEndian(tag[12..]) / 256.0;
                    if (gamma <= 0) { throw new InvalidDataException("ICC tone curve gamma must be positive."); }
                }
                else if (entries > 1)
                {
                    count = (int)entries; precision = 2; table = data.Slice(12, count * 2);
                }
                return;
            }
            if (!tag.StartsWith("para"u8)) { throw new NotSupportedException("ICC tone curve type is not supported."); }
            function = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
            int length = function switch
            {
                0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7,
                _ => throw new NotSupportedException("ICC parametric tone curve function is not supported.")
            };
            EncodedLength = 12 + length * 4;
            if (tag.Length < EncodedLength || BinaryPrimitives.ReadUInt16BigEndian(tag[10..]) != 0)
            { throw new InvalidDataException("ICC parametric tone curve is truncated or invalid."); }
            parameters = new double[7];
            for (int i = 0; i < length; i++) { parameters[i] = Fixed(tag[(12 + i * 4)..]); }
            if (parameters[0] <= 0 || (function != 0 && parameters[1] <= 0))
            { throw new InvalidDataException("ICC parametric tone curve has invalid gamma or scale."); }
        }

        private IccToneCurve(ReadOnlyMemory<byte> table, int precision)
        {
            if (precision is not (1 or 2) || table.Length % precision != 0 || table.Length / precision < 2)
            { throw new InvalidDataException("ICC lookup curve has an invalid size or precision."); }
            this.table = table; this.precision = precision; count = table.Length / precision;
            EncodedLength = table.Length;
        }

        internal static IccToneCurve FromTable(ReadOnlyMemory<byte> table, int precision) => new(table, precision);
        internal static double[] Sample(ReadOnlyMemory<byte> data, CancellationToken token) => new IccToneCurve(data).SampleBytes(token);
        internal double[] SampleBytes(CancellationToken token)
        {
            double[] values = new double[256];
            for (int i = 0; i < values.Length; i++) { token.ThrowIfCancellationRequested(); values[i] = Evaluate(i / 255.0); }
            return values;
        }

        internal double Evaluate(double x)
        {
            x = Math.Clamp(x, 0, 1);
            if (count != 0)
            {
                double position = x * (count - 1);
                int index = Math.Min((int)position, count - 2);
                double first = Entry(index), next = Entry(index + 1);
                return first + (next - first) * (position - index);
            }
            if (parameters is null) { return gamma == 1 ? x : Math.Pow(x, gamma); }
            double[] p = parameters;
            double value = function switch
            {
                0 => Math.Pow(x, p[0]),
                1 => x >= -p[2] / p[1] ? Power(p[1] * x + p[2], p[0]) : 0,
                2 => x >= -p[2] / p[1] ? Power(p[1] * x + p[2], p[0]) + p[3] : p[3],
                3 => x >= p[4] ? Power(p[1] * x + p[2], p[0]) : p[3] * x,
                4 => x >= p[4] ? Power(p[1] * x + p[2], p[0]) + p[5] : p[3] * x + p[6],
                _ => throw new InvalidDataException("Invalid ICC parametric tone curve function.")
            };
            if (!double.IsFinite(value)) { throw new InvalidDataException("ICC tone curve is not finite."); }
            return Math.Clamp(value, 0, 1);
        }

        private double Entry(int index) => precision switch
        {
            1 => table.Span[index] / 255.0,
            2 => BinaryPrimitives.ReadUInt16BigEndian(table.Span[(index * 2)..]) / 65535.0,
            _ => throw new InvalidOperationException("Invalid ICC lookup precision.")
        };
        internal static double Fixed(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadInt32BigEndian(value) / 65536.0;
        private static double Power(double value, double gamma) => value < 0
            ? throw new InvalidDataException("ICC parametric tone curve is undefined over the input range.") : Math.Pow(value, gamma);
    }
}
