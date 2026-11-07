// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats
{
    /// <summary>Three borrowed, immutable codec headers in Xiph lacing order.</summary>
    public class XiphLacedHeaders
    {
        private XiphLacedHeaders(ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second, ReadOnlyMemory<byte> third)
        { First = first; Second = second; Third = third; }

        public ReadOnlyMemory<byte> First { get; }
        public ReadOnlyMemory<byte> Second { get; }
        public ReadOnlyMemory<byte> Third { get; }

        public static XiphLacedHeaders Read(ReadOnlyMemory<byte> configuration)
        {
            ReadOnlySpan<byte> bytes = configuration.Span;
            if (bytes.IsEmpty || bytes[0] != 2) { throw new InvalidDataException("The configuration requires three Xiph-laced headers."); }
            int offset = 1;
            int first = ReadLength(bytes, ref offset), second = ReadLength(bytes, ref offset);
            if (first > bytes.Length - offset || second > bytes.Length - offset - first)
            { throw new InvalidDataException("A Xiph header extends past its configuration."); }
            return new(configuration.Slice(offset, first), configuration.Slice(offset + first, second), configuration[(offset + first + second)..]);
        }

        public static byte[] Write(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third)
        {
            byte[] result = new byte[checked(3 + first.Length / 255 + second.Length / 255 + first.Length + second.Length + third.Length)];
            result[0] = 2;
            int offset = 1;
            WriteLength(first.Length, result, ref offset); WriteLength(second.Length, result, ref offset);
            first.CopyTo(result.AsSpan(offset)); offset += first.Length;
            second.CopyTo(result.AsSpan(offset)); offset += second.Length;
            third.CopyTo(result.AsSpan(offset));
            return result;
        }

        private static int ReadLength(ReadOnlySpan<byte> bytes, ref int offset)
        {
            int length = 0;
            while (true)
            {
                if (offset == bytes.Length) { throw new InvalidDataException("The Xiph header lacing is truncated."); }
                byte value = bytes[offset++]; length = checked(length + value);
                if (value != 255) { return length; }
            }
        }

        private static void WriteLength(int length, Span<byte> bytes, ref int offset)
        {
            while (length >= 255) { bytes[offset++] = 255; length -= 255; }
            bytes[offset++] = (byte)length;
        }
    }
}
