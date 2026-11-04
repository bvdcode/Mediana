// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mediana.Drawing.Fonts
{
    internal static class CffData
    {
        internal static ReadOnlyMemory<byte>[] Index(ReadOnlyMemory<byte> data, ref int position)
        {
            Check(data.Length, position, 2);
            int count = BinaryPrimitives.ReadUInt16BigEndian(data.Span[position..]); position += 2;
            if (count == 0) { return []; }
            Check(data.Length, position, 1);
            int size = data.Span[position++];
            if (size is < 1 or > 4) { throw new InvalidDataException("Invalid CFF INDEX offset size."); }
            Check(data.Length, position, (count + 1) * size);
            int offsets = position;
            int start = position + (count + 1) * size;
            int Offset(int index)
            {
                uint value = 0;
                foreach (byte item in data.Span.Slice(offsets + index * size, size)) { value = (value << 8) | item; }
                if (value == 0 || value > data.Length - start + 1) { throw new InvalidDataException("CFF INDEX offset exceeds its table."); }
                return (int)value - 1;
            }
            int previous = Offset(0);
            if (previous != 0) { throw new InvalidDataException("CFF INDEX must start at offset one."); }
            ReadOnlyMemory<byte>[] result = new ReadOnlyMemory<byte>[count];
            for (int i = 0; i < count; i++)
            {
                int next = Offset(i + 1);
                if (next < previous) { throw new InvalidDataException("CFF INDEX offsets are not ordered."); }
                result[i] = data.Slice(start + previous, next - previous); previous = next;
            }
            position = start + previous;
            return result;
        }

        internal static Dictionary<int, double[]> Dictionary(ReadOnlySpan<byte> data)
        {
            Dictionary<int, double[]> result = [];
            List<double> operands = [];
            int position = 0;
            while (position < data.Length)
            {
                int code = data[position++];
                if (code == 255) { throw new InvalidDataException("Reserved CFF DICT number."); }
                if (code >= 32 || code is 28 or 29 or 30)
                {
                    operands.Add(Number(data, ref position, code));
                    if (operands.Count > 48) { throw new InvalidDataException("CFF DICT operand limit exceeded."); }
                    continue;
                }
                if (code == 12) { Check(data.Length, position, 1); code = 1200 + data[position++]; }
                if (!result.TryAdd(code, operands.ToArray())) { throw new InvalidDataException("Duplicate CFF DICT operator."); }
                operands.Clear();
            }
            if (operands.Count != 0) { throw new InvalidDataException("Unterminated CFF DICT operands."); }
            return result;
        }

        internal static int Integer(double value)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value < 0 || value > int.MaxValue)
            { throw new InvalidDataException("Invalid CFF offset or count."); }
            return (int)value;
        }

        internal static double Number(ReadOnlySpan<byte> data, ref int position, int code)
        {
            if (code is >= 32 and <= 246) { return code - 139; }
            if (code is >= 247 and <= 254)
            {
                Check(data.Length, position, 1);
                int value = data[position++];
                return code <= 250 ? (code - 247) * 256 + value + 108 : -(code - 251) * 256 - value - 108;
            }
            if (code is 28 or 29 or 255)
            {
                int length = code == 28 ? 2 : 4;
                Check(data.Length, position, length);
                double value = code == 28 ? BinaryPrimitives.ReadInt16BigEndian(data[position..]) : BinaryPrimitives.ReadInt32BigEndian(data[position..]);
                position += length;
                return code == 255 ? value / 65536 : value;
            }
            if (code != 30) { throw new InvalidDataException("Invalid CFF number."); }
            StringBuilder text = new();
            bool complete = false;
            while (!complete)
            {
                Check(data.Length, position, 1);
                int item = data[position++];
                for (int shift = 4; shift >= 0; shift -= 4)
                {
                    int digit = (item >> shift) & 15;
                    if (digit == 15) { complete = true; break; }
                    text.Append(digit switch
                    {
                        <= 9 => ((char)('0' + digit)).ToString(),
                        10 => ".",
                        11 => "E",
                        12 => "E-",
                        14 => "-",
                        _ => throw new InvalidDataException("Reserved CFF real-number nibble.")
                    });
                    if (text.Length > 64) { throw new InvalidDataException("CFF real number is too long."); }
                }
            }
            if (!double.TryParse(text.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            { throw new InvalidDataException("Invalid CFF real number."); }
            return number;
        }

        internal static void Check(int length, int position, int count)
        {
            if (position < 0 || count < 0 || position > length - count) { throw new InvalidDataException("Truncated CFF data."); }
        }
    }
}
