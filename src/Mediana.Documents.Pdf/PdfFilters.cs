// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;

namespace Mediana.Documents.Pdf
{
    internal static class PdfFilters
    {
        internal static byte[] Decode(ReadOnlyMemory<byte> source, string filter, PdfValue parameters, int limit, CancellationToken token)
        {
            byte[] result;
            switch (filter)
            {
                case "FlateDecode":
                case "Fl":
                    using (MemoryStream input = new(source.ToArray(), false))
                    using (ZLibStream inflater = new(input, CompressionMode.Decompress))
                    using (MemoryStream output = new())
                    {
                        byte[] buffer = new byte[8192];
                        int count;
                        while ((count = inflater.Read(buffer)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            Check(output.Length + count, limit);
                            output.Write(buffer, 0, count);
                        }
                        result = output.ToArray();
                    }
                    return Predict(result, parameters, limit, token);
                case "ASCIIHexDecode":
                case "AHx":
                    List<byte> hex = [];
                    int high = -1;
                    bool ended = false;
                    foreach (byte c in source.Span)
                    {
                        token.ThrowIfCancellationRequested();
                        if (c == '>') { ended = true; break; }
                        if (PdfSyntax.White(c)) { continue; }
                        int value = PdfSyntax.Hex(c);
                        if (high < 0) { high = value; }
                        else { Check(hex.Count + 1L, limit); hex.Add((byte)((high << 4) | value)); high = -1; }
                    }
                    if (!ended) { throw new InvalidDataException("Missing ASCIIHex end marker."); }
                    if (high >= 0) { Check(hex.Count + 1L, limit); hex.Add((byte)(high << 4)); }
                    return hex.ToArray();
                case "ASCII85Decode":
                case "A85":
                    return Ascii85(source.Span, limit, token);
                case "RunLengthDecode":
                case "RL":
                    using (MemoryStream output = new())
                    {
                        int position = 0;
                        while (position < source.Length)
                        {
                            token.ThrowIfCancellationRequested();
                            int control = source.Span[position++];
                            if (control == 128) { return output.ToArray(); }
                            int count = control < 128 ? control + 1 : 257 - control;
                            int encoded = control < 128 ? count : 1;
                            if (position + encoded > source.Length) { throw new InvalidDataException("Truncated RunLength data."); }
                            Check(output.Length + count, limit);
                            if (control < 128) { output.Write(source.Span.Slice(position, count)); }
                            else { for (int i = 0; i < count; i++) { output.WriteByte(source.Span[position]); } }
                            position += encoded;
                        }
                        throw new InvalidDataException("Missing RunLength end marker.");
                    }
                default: throw new NotSupportedException("Unsupported PDF stream filter: " + filter);
            }
        }
        private static byte[] Ascii85(ReadOnlySpan<byte> source, int limit, CancellationToken token)
        {
            using MemoryStream output = new();
            ulong tuple = 0;
            int digits = 0;
            bool ended = false;
            for (int i = 0; i < source.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                byte c = source[i];
                if (PdfSyntax.White(c)) { continue; }
                if (c == '~')
                {
                    if (++i >= source.Length || source[i] != '>') { throw new InvalidDataException("Invalid ASCII85 terminator."); }
                    ended = true; break;
                }
                if (c == 'z')
                {
                    if (digits != 0) { throw new InvalidDataException("ASCII85 z inside a tuple."); }
                    Check(output.Length + 4, limit); output.Write(new byte[4]); continue;
                }
                if (c is < 33 or > 117) { throw new InvalidDataException("Invalid ASCII85 digit."); }
                tuple = tuple * 85 + c - 33U;
                if (++digits == 5)
                {
                    WriteTuple(output, tuple, 4, limit); tuple = 0; digits = 0;
                }
            }
            if (!ended || digits == 1) { throw new InvalidDataException("Invalid ASCII85 ending."); }
            if (digits > 1)
            {
                int count = digits - 1;
                while (digits++ < 5) { tuple = tuple * 85 + 84; }
                WriteTuple(output, tuple, count, limit);
            }
            return output.ToArray();
        }
        private static void WriteTuple(MemoryStream output, ulong tuple, int count, int limit)
        {
            if (tuple > uint.MaxValue) { throw new InvalidDataException("ASCII85 tuple overflow."); }
            Check(output.Length + count, limit);
            for (int i = 0; i < count; i++) { output.WriteByte((byte)(tuple >> (24 - i * 8))); }
        }
        private static void Check(long length, int limit)
        { if (length > limit) { throw new InvalidDataException("PDF stream exceeds the decoded byte limit."); } }
        private static byte[] Predict(byte[] data, PdfValue parameters, int limit, CancellationToken token)
        {
            int predictor = Number(parameters, "Predictor", 1);
            if (predictor == 1) { return data; }
            int colors = Number(parameters, "Colors", 1), columns = Number(parameters, "Columns", 1), bits = Number(parameters, "BitsPerComponent", 8);
            if (colors <= 0 || columns <= 0 || bits is not (1 or 2 or 4 or 8 or 16)) { throw new InvalidDataException("Invalid predictor dimensions."); }
            long rowSize = ((long)colors * columns * bits + 7) / 8;
            Check(rowSize, limit);
            int row = checked((int)rowSize), pixel = checked((colors * bits + 7) / 8);
            if (predictor == 2)
            {
                if (bits != 8) { throw new NotSupportedException("TIFF prediction currently requires 8-bit components."); }
                if (data.Length % row != 0) { throw new InvalidDataException("Incomplete TIFF predictor row."); }
                for (int start = 0; start < data.Length; start += row)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = pixel; x < row; x++) { data[start + x] = unchecked((byte)(data[start + x] + data[start + x - pixel])); }
                }
                return data;
            }
            if (predictor is < 10 or > 15) { throw new NotSupportedException("Unsupported PDF predictor."); }
            if (data.Length % (row + 1) != 0) { throw new InvalidDataException("Incomplete PNG predictor row."); }
            byte[] output = new byte[data.Length / (row + 1) * row];
            for (int y = 0; y < output.Length / row; y++)
            {
                token.ThrowIfCancellationRequested();
                int filter = data[y * (row + 1)];
                for (int x = 0; x < row; x++)
                {
                    int left = x >= pixel ? output[y * row + x - pixel] : 0;
                    int above = y > 0 ? output[(y - 1) * row + x] : 0;
                    int corner = x >= pixel && y > 0 ? output[(y - 1) * row + x - pixel] : 0;
                    int estimate = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => above,
                        3 => (left + above) / 2,
                        4 => Paeth(left, above, corner),
                        _ => throw new InvalidDataException("Invalid PNG predictor filter.")
                    };
                    output[y * row + x] = unchecked((byte)(data[y * (row + 1) + 1 + x] + estimate));
                }
            }
            return output;
        }
        private static int Number(PdfValue value, string key, int initial) => value.Get(key).Kind == PdfValueKind.Null ? initial : value.Get(key).Integer();
        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, x = Math.Abs(p - a), y = Math.Abs(p - b), z = Math.Abs(p - c);
            return x <= y && x <= z ? a : y <= z ? b : c;
        }
    }
}
