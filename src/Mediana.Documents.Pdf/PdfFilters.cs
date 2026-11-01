// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.IO;

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
                    using (ReadOnlyMemoryStream input = new(source))
                    using (ZLibStream inflater = new(input, CompressionMode.Decompress))
                    {
                        result = BoundedStreamReader.Read(inflater, limit, token);
                    }
                    return Predict(result, parameters, limit, token);
                default: return PdfByteFilters.Decode(source.Span, filter, limit, token);
            }
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
