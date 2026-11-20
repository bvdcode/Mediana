// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Documents.Pdf
{
    internal class PdfUnicodeMap
    {
        private static readonly Encoding Utf16 = new UnicodeEncoding(true, false, true);
        private readonly PdfUnicodeRange[] _ranges;
        private readonly Dictionary<int, string?> _used = [];
        internal int Count { get; }

        private PdfUnicodeMap(PdfUnicodeRange[] ranges)
        {
            _ranges = ranges;
            Count = ranges.Sum(range => range.Last - range.First + 1);
        }

        internal bool TryGetValue(int code, out string? value)
        {
            if (_used.TryGetValue(code, out value)) { return true; }
            int low = 0, high = _ranges.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                PdfUnicodeRange range = _ranges[middle];
                if (code < range.First) { high = middle - 1; }
                else if (code > range.Last) { low = middle + 1; }
                else
                {
                    if (code == range.First) { value = ScalarBytes(range.Destination); }
                    else
                    {
                        byte[] destination = range.Destination.ToArray();
                        Increment(destination, code - range.First);
                        value = ScalarBytes(destination);
                    }
                    _used.Add(code, value);
                    return true;
                }
            }
            value = null;
            return false;
        }

        internal bool TryGetScalar(int code, out int scalar)
        {
            scalar = -1;
            if (!TryGetValue(code, out string? text)) { return false; }
            if (text is not null)
            {
                Rune rune = Rune.GetRuneAt(text, 0);
                if (rune.Utf16SequenceLength == text.Length) { scalar = rune.Value; }
            }
            return true;
        }

        internal static PdfUnicodeMap Read(PdfStorage storage, PdfValue reference, bool composite, CancellationToken token, Action<int> charge, Action<string> warning)
        {
            ReadOnlyMemory<byte> bytes = storage.Decode(storage.Object(reference, token), token);
            PdfSyntax syntax = new(bytes, storage.Budget, token);
            List<PdfUnicodeRange> mappings = [];
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
                        if (count < 0 || count > storage.Budget.Options.MaximumOperations) { throw new InvalidDataException("Invalid PDF CMap block size."); }
                        for (int i = 0; i < count; i++)
                        {
                            int first = Code(syntax.Read());
                            if (value.Text == "beginbfchar")
                            {
                                Add(first, first, Destination(syntax.Read()));
                                continue;
                            }
                            int last = Code(syntax.Read());
                            if (last < first) { throw new InvalidDataException("Reversed PDF CMap range."); }
                            if (value.Text == "begincodespacerange") { spaces.Add((first, last)); continue; }
                            PdfValue target = syntax.Read();
                            if (target.Kind == PdfValueKind.Array)
                            {
                                if (target.Items.Length != last - first + 1) { throw new InvalidDataException("PDF CMap range length mismatch."); }
                                for (int code = first; code <= last; code++) { Add(code, code, Destination(target.Items[code - first])); }
                            }
                            else
                            {
                                if (target.Kind != PdfValueKind.String || target.Bytes.Length == 0 || target.Bytes.Length % 2 != 0)
                                { throw new InvalidDataException("Invalid PDF ToUnicode destination."); }
                                int range = last - first;
                                if (range > 255 - target.Bytes[^1])
                                { warning("A PDF ToUnicode range crosses a byte boundary; sequential UTF-16BE values were recovered."); }
                                byte[] lastDestination = target.Bytes.ToArray();
                                Increment(lastDestination, range);
                                Add(first, last, target.Bytes);
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
            if (spaces.Count == 0) { throw new InvalidDataException("PDF ToUnicode has no code space."); }
            List<(int First, int Last)> permitted = [];
            foreach ((int first, int last) in spaces.OrderBy(space => space.First))
            {
                charge(last - first + 1);
                if (permitted.Count != 0 && first <= permitted[^1].Last + 1)
                { permitted[^1] = (permitted[^1].First, Math.Max(permitted[^1].Last, last)); }
                else { permitted.Add((first, last)); }
            }
            PdfUnicodeRange[] ordered = mappings.OrderBy(mapping => mapping.First).ToArray();
            int previousEnd = -1, spaceIndex = 0;
            foreach (PdfUnicodeRange mapping in ordered)
            {
                if (mapping.First <= previousEnd) { throw new InvalidDataException("Overlapping PDF ToUnicode mappings."); }
                previousEnd = mapping.Last;
                while (spaceIndex < permitted.Count && permitted[spaceIndex].Last < mapping.First) { spaceIndex++; }
                if (spaceIndex == permitted.Count || mapping.First < permitted[spaceIndex].First || mapping.Last > permitted[spaceIndex].Last)
                { throw new InvalidDataException("PDF ToUnicode mapping lies outside its code space."); }
            }
            return new(ordered);

            int Code(PdfValue value)
            {
                int width = composite ? 2 : 1;
                if (value.Kind != PdfValueKind.String || value.Bytes.Length != width)
                { throw new NotSupportedException("PDF ToUnicode source width does not match the font encoding."); }
                return width == 2 ? BinaryPrimitives.ReadUInt16BigEndian(value.Bytes) : value.Bytes[0];
            }
            void Add(int first, int last, byte[] destination)
            {
                charge(last - first + 1);
                mappings.Add(new(first, last, destination));
            }
        }

        private static byte[] Destination(PdfValue value)
        {
            if (value.Kind != PdfValueKind.String || value.Bytes.Length == 0 || value.Bytes.Length > 512 || value.Bytes.Length % 2 != 0)
            { throw new InvalidDataException("Invalid PDF ToUnicode destination."); }
            return value.Bytes;
        }

        private static void Increment(Span<byte> bytes, int amount)
        {
            for (int digit = bytes.Length - 1; digit >= 0 && amount != 0; digit--)
            {
                int value = bytes[digit] + (amount & 255);
                bytes[digit] = (byte)value;
                amount = (amount >> 8) + (value >> 8);
            }
            if (amount != 0) { throw new InvalidDataException("PDF ToUnicode destination overflow."); }
        }

        private static string? ScalarBytes(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                char character = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes[i..]);
                if (!char.IsSurrogate(character)) { continue; }
                if (!char.IsHighSurrogate(character) || i + 3 >= bytes.Length
                    || !char.IsLowSurrogate((char)BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 2)..]))) { return null; }
                i += 2;
            }
            return Utf16.GetString(bytes);
        }
    }
}
