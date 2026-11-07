// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Processing
{
    internal static class IccToneCurve
    {
        internal static double[] Sample(ReadOnlySpan<byte> tag, CancellationToken token)
        {
            if (tag.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(tag[4..]) != 0)
            {
                throw new InvalidDataException("ICC tone curve header is invalid.");
            }
            double[] values = new double[256];
            if (tag.StartsWith("curv"u8))
            {
                uint count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
                if (count > (tag.Length - 12) / 2)
                {
                    throw new InvalidDataException("ICC sampled tone curve is truncated.");
                }
                double gamma = count == 1 ? BinaryPrimitives.ReadUInt16BigEndian(tag[12..]) / 256.0 : 1;
                if (gamma <= 0) { throw new InvalidDataException("ICC tone curve gamma must be positive."); }
                for (int sample = 0; sample < values.Length; sample++)
                {
                    token.ThrowIfCancellationRequested();
                    double x = sample / 255.0;
                    if (count <= 1) { values[sample] = Math.Pow(x, gamma); }
                    else
                    {
                        double position = x * (count - 1);
                        int index = Math.Min((int)position, (int)count - 2);
                        double first = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + index * 2)..]) / 65535.0;
                        double next = BinaryPrimitives.ReadUInt16BigEndian(tag[(14 + index * 2)..]) / 65535.0;
                        values[sample] = first + (next - first) * (position - index);
                    }
                }
                return values;
            }
            if (!tag.StartsWith("para"u8)) { throw new NotSupportedException("ICC tone curve type is not supported."); }
            int function = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
            int parameters = function switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => throw new NotSupportedException("ICC parametric tone curve function is not supported.")
            };
            if (tag.Length < 12 + parameters * 4 || BinaryPrimitives.ReadUInt16BigEndian(tag[10..]) != 0)
            {
                throw new InvalidDataException("ICC parametric tone curve is truncated or invalid.");
            }
            Span<double> p = stackalloc double[7];
            p.Clear();
            for (int i = 0; i < parameters; i++) { p[i] = Fixed(tag[(12 + i * 4)..]); }
            if (p[0] <= 0 || (function != 0 && p[1] <= 0))
            {
                throw new InvalidDataException("ICC parametric tone curve has invalid gamma or scale.");
            }
            for (int sample = 0; sample < values.Length; sample++)
            {
                token.ThrowIfCancellationRequested();
                double x = sample / 255.0;
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
                values[sample] = Math.Clamp(value, 0, 1);
            }
            return values;
        }

        internal static double Fixed(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadInt32BigEndian(value) / 65536.0;

        private static double Power(double value, double gamma) => value < 0
            ? throw new InvalidDataException("ICC parametric tone curve is undefined over the input range.") : Math.Pow(value, gamma);
    }
}
