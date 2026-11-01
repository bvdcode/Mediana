// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Buffers.Binary;

namespace Mediana.Documents.Pdf
{
    internal static class PdfByteFilters
    {
        private const int MaximumHexBlockLength = 8192;
        private static readonly SearchValues<byte> HexDigits = SearchValues.Create("0123456789ABCDEFabcdef"u8);

        internal static byte[] Decode(ReadOnlySpan<byte> source, string filter, int limit, CancellationToken token)
        {
            PdfFilterOutput measurement = new(limit);
            Read(source, filter, ref measurement, token);
            token.ThrowIfCancellationRequested();
            byte[] result = new byte[measurement.Length];
            PdfFilterOutput output = new(result);
            Read(source, filter, ref output, token);
            return result;
        }

        private static void Read(ReadOnlySpan<byte> source, string filter, ref PdfFilterOutput output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (filter)
            {
                case "ASCIIHexDecode":
                case "AHx": Hex(source, ref output, token); break;
                case "ASCII85Decode":
                case "A85": Ascii85(source, ref output, token); break;
                case "RunLengthDecode":
                case "RL": RunLength(source, ref output, token); break;
                default: throw new NotSupportedException("Unsupported PDF stream filter: " + filter);
            }
        }

        private static void Hex(ReadOnlySpan<byte> source, ref PdfFilterOutput output, CancellationToken token)
        {
            int high = -1;
            int position = 0;
            while (position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                byte value = source[position];
                if (value == '>')
                {
                    if (high >= 0) { output.Write((byte)(high << 4)); }
                    return;
                }
                if (PdfSyntax.White(value)) { position++; continue; }
                if (high >= 0)
                {
                    output.Write((byte)((high << 4) | PdfSyntax.Hex(value)));
                    high = -1;
                    position++;
                    continue;
                }
                ReadOnlySpan<byte> block = source.Slice(position, Math.Min(MaximumHexBlockLength, source.Length - position));
                int length = block.IndexOfAnyExcept(HexDigits);
                if (length < 0) { length = block.Length; }
                if (length == 0) { throw new InvalidDataException("Invalid PDF hexadecimal digit."); }
                int complete = length & ~1;
                Span<byte> destination = output.Reserve(complete / 2);
                if (!destination.IsEmpty)
                {
                    OperationStatus status = Convert.FromHexString(block[..complete], destination, out int consumed, out int written);
                    if (status != OperationStatus.Done || consumed != complete || written != destination.Length)
                    {
                        throw new InvalidDataException("Invalid PDF hexadecimal block.");
                    }
                }
                position += complete;
                if (complete < length) { high = PdfSyntax.Hex(source[position++]); }
            }
            throw new InvalidDataException("Missing ASCIIHex end marker.");
        }

        private static void Ascii85(ReadOnlySpan<byte> source, ref PdfFilterOutput output, CancellationToken token)
        {
            ulong tuple = 0;
            int digits = 0;
            Span<byte> bytes = stackalloc byte[4];
            for (int i = 0; i < source.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                byte value = source[i];
                if (PdfSyntax.White(value)) { continue; }
                if (value == '~')
                {
                    if (++i >= source.Length || source[i] != '>') { throw new InvalidDataException("Invalid ASCII85 terminator."); }
                    if (digits == 1) { throw new InvalidDataException("Invalid ASCII85 ending."); }
                    if (digits > 1)
                    {
                        int count = digits - 1;
                        while (digits++ < 5) { tuple = tuple * 85 + 84; }
                        WriteTuple(tuple, count, bytes, ref output);
                    }
                    return;
                }
                if (value == 'z')
                {
                    if (digits != 0) { throw new InvalidDataException("ASCII85 z inside a tuple."); }
                    output.Repeat(0, 4);
                    continue;
                }
                if (value is < 33 or > 117) { throw new InvalidDataException("Invalid ASCII85 digit."); }
                tuple = tuple * 85 + value - 33U;
                if (++digits == 5)
                {
                    WriteTuple(tuple, 4, bytes, ref output);
                    tuple = 0;
                    digits = 0;
                }
            }
            throw new InvalidDataException("Invalid ASCII85 ending.");
        }

        private static void WriteTuple(ulong tuple, int count, scoped Span<byte> bytes, ref PdfFilterOutput output)
        {
            if (tuple > uint.MaxValue) { throw new InvalidDataException("ASCII85 tuple overflow."); }
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)tuple);
            output.Write(bytes[..count]);
        }

        private static void RunLength(ReadOnlySpan<byte> source, ref PdfFilterOutput output, CancellationToken token)
        {
            int position = 0;
            while (position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                int control = source[position++];
                if (control == 128) { return; }
                int count = control < 128 ? control + 1 : 257 - control;
                int encoded = control < 128 ? count : 1;
                if (encoded > source.Length - position) { throw new InvalidDataException("Truncated RunLength data."); }
                if (control < 128) { output.Write(source.Slice(position, count)); }
                else { output.Repeat(source[position], count); }
                position += encoded;
            }
            throw new InvalidDataException("Missing RunLength end marker.");
        }
    }
}
