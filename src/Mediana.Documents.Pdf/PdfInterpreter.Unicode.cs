// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private Dictionary<int, int> ReadToUnicode(PdfValue reference, bool composite)
        {
            ReadOnlyMemory<byte> bytes = storage.Decode(storage.Object(reference, token), token);
            PdfSyntax syntax = new(bytes, storage.Budget, token);
            Dictionary<int, int> mappings = [];
            List<(int First, int Last)> spaces = [];
            PdfValue previous = PdfValue.Null;
            while (true)
            {
                syntax.Skip();
                if (syntax.Position == bytes.Length) { break; }
                PdfValue value = syntax.Read();
                if (value.Kind != PdfValueKind.Keyword) { previous = value; continue; }
                switch (value.Text)
                {
                    case "usecmap": throw new NotSupportedException("Inherited PDF ToUnicode CMaps are not supported.");
                    case "begincodespacerange":
                    case "beginbfchar":
                    case "beginbfrange":
                        int count = previous.Integer();
                        if (count is < 1 or > 100) { throw new InvalidDataException("Invalid PDF CMap block size."); }
                        for (int i = 0; i < count; i++)
                        {
                            int first = Code(syntax.Read());
                            if (value.Text == "beginbfchar")
                            {
                                Add(first, Scalar(syntax.Read()));
                                continue;
                            }
                            int last = Code(syntax.Read());
                            if (last < first) { throw new InvalidDataException("Reversed PDF CMap range."); }
                            if (value.Text == "begincodespacerange") { spaces.Add((first, last)); continue; }
                            PdfValue target = syntax.Read();
                            if (target.Kind == PdfValueKind.Array)
                            {
                                if (target.Items.Length != last - first + 1) { throw new InvalidDataException("PDF CMap range length mismatch."); }
                                for (int code = first; code <= last; code++) { Add(code, Scalar(target.Items[code - first])); }
                            }
                            else
                            {
                                if (target.Kind != PdfValueKind.String || target.Bytes.Length == 0 || target.Bytes.Length % 2 != 0)
                                { throw new InvalidDataException("Invalid PDF ToUnicode destination."); }
                                int range = last - first;
                                if (range > 255 - target.Bytes[^1])
                                { _warnings.Add("A PDF ToUnicode range crosses a byte boundary; sequential UTF-16BE values were recovered."); }
                                byte[] destination = target.Bytes.ToArray();
                                for (int code = first; code <= last; code++)
                                {
                                    Add(code, ScalarBytes(destination));
                                    if (code < last)
                                    {
                                        int digit = destination.Length - 1;
                                        while (digit >= 0 && destination[digit] == 255) { destination[digit--] = 0; }
                                        if (digit < 0) { throw new InvalidDataException("PDF ToUnicode destination overflow."); }
                                        destination[digit]++;
                                    }
                                }
                            }
                        }
                        syntax.Expect(value.Text switch
                        {
                            "begincodespacerange" => "endcodespacerange",
                            "beginbfchar" => "endbfchar",
                            "beginbfrange" => "endbfrange",
                            _ => throw new InvalidDataException("Invalid PDF CMap block.")
                        });
                        break;
                }
                previous = value;
            }
            bool[] permitted = new bool[composite ? 65536 : 256];
            foreach ((int first, int last) in spaces)
            {
                ChargeOperations(last - first + 1);
                permitted.AsSpan(first, last - first + 1).Fill(true);
            }
            if (spaces.Count == 0 || mappings.Keys.Any(code => !permitted[code]))
            { throw new InvalidDataException("PDF ToUnicode mapping lies outside its code space."); }
            return mappings;

            int Code(PdfValue value)
            {
                int width = composite ? 2 : 1;
                if (value.Kind != PdfValueKind.String || value.Bytes.Length != width)
                { throw new NotSupportedException("PDF ToUnicode source width does not match the font encoding."); }
                return width == 2 ? BinaryPrimitives.ReadUInt16BigEndian(value.Bytes) : value.Bytes[0];
            }
            void Add(int code, int scalar)
            {
                ChargeOperations(1);
                if (!mappings.TryAdd(code, scalar)) { throw new InvalidDataException("Overlapping PDF ToUnicode mappings."); }
            }
        }

        private static int Scalar(PdfValue value)
        {
            if (value.Kind != PdfValueKind.String || value.Bytes.Length == 0 || value.Bytes.Length % 2 != 0)
            { throw new InvalidDataException("Invalid PDF ToUnicode destination."); }
            return ScalarBytes(value.Bytes);
        }

        private static int ScalarBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 2)
            {
                int scalar = BinaryPrimitives.ReadUInt16BigEndian(bytes);
                return Rune.IsValid(scalar) ? scalar : -1;
            }
            if (bytes.Length == 4)
            {
                char high = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes);
                char low = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);
                if (char.IsSurrogatePair(high, low)) { return char.ConvertToUtf32(high, low); }
            }
            return -1;
        }
    }
}
