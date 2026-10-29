// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;

namespace Mediana.Documents.Pdf
{
    internal class PdfStorage
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly Dictionary<int, PdfXrefEntry> _xref = [];
        private readonly Dictionary<int, PdfObject> _objects = [];
        private readonly Dictionary<PdfObject, byte[]> _streams = [];
        private readonly HashSet<int> _active = [];
        private readonly HashSet<int> _sections = [];
        internal PdfBudget Budget { get; }
        internal PdfValue Trailer { get; }
        internal PdfStorage(byte[] data, PdfReaderOptions options, CancellationToken token)
        {
            _data = data;
            Budget = new(options);
            if (!data.AsSpan().StartsWith("%PDF-"u8)) { throw new InvalidDataException("Missing PDF header."); }
            int marker = data.AsSpan().LastIndexOf("startxref"u8);
            if (marker < 0) { throw new InvalidDataException("Missing PDF cross-reference offset."); }
            PdfSyntax end = Syntax(marker + 9, token);
            int offset = end.Read().Integer();
            Trailer = ReadSection(offset, token);
            if (Trailer.Get("Encrypt").Kind != PdfValueKind.Null) { throw new NotSupportedException("Encrypted PDF documents are not supported."); }
        }
        private PdfSyntax Syntax(int position, CancellationToken token)
        {
            if ((uint)position >= _data.Length) { throw new InvalidDataException("PDF object offset is outside the input."); }
            return new(_data, Budget, token) { Position = position };
        }
        private PdfValue ReadSection(int offset, CancellationToken token, bool hybrid = false)
        {
            if (_sections.Count >= Budget.Options.MaximumDepth || !_sections.Add(offset)) { throw new InvalidDataException("Cyclic or excessive PDF cross-reference history."); }
            PdfSyntax syntax = Syntax(offset, token);
            int start = syntax.Position;
            string word = syntax.Word();
            HashSet<int> local = [];
            PdfValue trailer;
            if (word == "xref")
            {
                while (true)
                {
                    word = syntax.Word();
                    if (word == "trailer") { break; }
                    int first = ParseInteger(word), count = ParseInteger(syntax.Word());
                    CheckRange(first, count);
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        int position = ParseInteger(syntax.Word()), gen = ParseInteger(syntax.Word());
                        string state = syntax.Word();
                        if (gen > 65535 || state is not ("n" or "f")) { throw new InvalidDataException("Invalid PDF cross-reference entry."); }
                        if (!local.Add(first + i)) { throw new InvalidDataException("Overlapping PDF cross-reference subsections."); }
                        _xref.TryAdd(first + i, new(state == "n" ? 1 : 0, position, gen));
                    }
                }
                trailer = syntax.Read();
                if (trailer.Get("XRefStm").Kind != PdfValueKind.Null) { ReadSection(trailer.Get("XRefStm").Integer(), token, hybrid: true); }
            }
            else
            {
                syntax.Position = start;
                PdfObject stream = ReadIndirect(syntax, null, token);
                trailer = stream.Value;
                if (trailer.Get("Type").Name() != "XRef") { throw new InvalidDataException("Expected a PDF cross-reference stream."); }
                byte[] entries = Decode(stream, token);
                PdfValue[] widths = trailer.Get("W").Items;
                if (widths.Length != 3) { throw new InvalidDataException("Invalid PDF cross-reference widths."); }
                int[] sizes = widths.Select(x => x.Integer()).ToArray();
                if (sizes.Any(x => x < 0 || x > 8)) { throw new InvalidDataException("Invalid PDF cross-reference field size."); }
                PdfValue ranges = trailer.Get("Index");
                int[] index = ranges.Kind == PdfValueKind.Null ? [0, trailer.Get("Size").Integer()] : ranges.Items.Select(x => x.Integer()).ToArray();
                if (index.Length % 2 != 0) { throw new InvalidDataException("Invalid PDF cross-reference index."); }
                int cursor = 0;
                for (int pair = 0; pair < index.Length; pair += 2)
                {
                    int first = index[pair], count = index[pair + 1]; CheckRange(first, count);
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        int kind = sizes[0] == 0 ? 1 : Field(entries, ref cursor, sizes[0]);
                        int field = Field(entries, ref cursor, sizes[1]), last = Field(entries, ref cursor, sizes[2]);
                        if (kind is < 0 or > 2) { throw new NotSupportedException("Unknown PDF cross-reference entry type."); }
                        if (!local.Add(first + i)) { throw new InvalidDataException("Overlapping PDF cross-reference stream ranges."); }
                        PdfXrefEntry entry = new(kind, field, last);
                        if (!_xref.TryGetValue(first + i, out PdfXrefEntry prior) || hybrid && prior.Kind == 0 && kind == 2) { _xref[first + i] = entry; }
                    }
                }
                if (cursor != entries.Length) { throw new InvalidDataException("Cross-reference stream length mismatch."); }
            }
            if (trailer.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("Invalid PDF trailer."); }
            if (trailer.Get("Prev").Kind != PdfValueKind.Null) { ReadSection(trailer.Get("Prev").Integer(), token); }
            return trailer;
        }
        private void CheckRange(int first, int count)
        { if (first < 0 || count < 0 || (long)first + count > Budget.Options.MaximumObjects) { throw new InvalidDataException("PDF object range exceeds its limit."); } }
        private static int ParseInteger(string text)
        { if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) { throw new InvalidDataException("Invalid PDF offset or count."); } return value; }
        private static int Field(byte[] data, ref int position, int width)
        {
            if (position > data.Length - width) { throw new InvalidDataException("Truncated cross-reference stream."); }
            ulong value = 0;
            for (int i = 0; i < width; i++) { value = (value << 8) | data[position++]; }
            if (value > int.MaxValue) { throw new InvalidDataException("PDF cross-reference field is too large."); }
            return (int)value;
        }
        internal PdfValue Resolve(PdfValue value, CancellationToken token)
        {
            int depth = 0;
            while (value.Kind == PdfValueKind.Reference)
            {
                if (++depth > Budget.Options.MaximumDepth) { throw new InvalidDataException("Cyclic PDF reference."); }
                value = Object(value, token).Value;
            }
            return value;
        }
        internal PdfObject Object(PdfValue reference, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (reference.Kind != PdfValueKind.Reference) { throw new InvalidDataException("Expected an indirect PDF object."); }
            int number = reference.ObjectNumber;
            if (!_xref.TryGetValue(number, out PdfXrefEntry entry) || entry.Kind == 0 || reference.Generation != (entry.Kind == 2 ? 0 : entry.GenerationOrIndex))
            { throw new InvalidDataException("Missing or invalid PDF object reference."); }
            if (_objects.TryGetValue(number, out PdfObject? cached)) { return cached; }
            if (_active.Count >= Budget.Options.MaximumDepth || !_active.Add(number)) { throw new InvalidDataException("Cyclic PDF object dependency."); }
            try
            {
                PdfObject result;
                if (entry.Kind == 1)
                {
                    result = ReadIndirect(Syntax(entry.Offset, token), reference, token);
                }
                else if (entry.Kind == 2)
                {
                    PdfObject container = Object(new(PdfValueKind.Reference) { ObjectNumber = entry.Offset }, token);
                    if (container.Value.Get("Type").Name() != "ObjStm") { throw new InvalidDataException("Invalid PDF object stream."); }
                    int count = container.Value.Get("N").Integer(), first = container.Value.Get("First").Integer();
                    CheckRange(0, count);
                    byte[] data = Decode(container, token);
                    if (first < 0 || first > data.Length || entry.GenerationOrIndex >= count) { throw new InvalidDataException("Invalid object stream index."); }
                    PdfSyntax header = new(data.AsMemory(0, first), Budget, token);
                    int target = -1;
                    for (int i = 0; i < count; i++)
                    {
                        int id = header.Read().Integer(), position = header.Read().Integer();
                        if (position < 0 || position >= data.Length - first) { throw new InvalidDataException("Invalid compressed object offset."); }
                        if (i == entry.GenerationOrIndex)
                        { if (id != number) { throw new InvalidDataException("Object stream number mismatch."); } target = position; }
                    }
                    PdfSyntax body = new(data, Budget, token) { Position = checked(first + target) };
                    result = new(body.Read(), default);
                }
                else { throw new InvalidDataException("Unknown PDF object entry."); }
                _objects.Add(number, result); return result;
            }
            finally { _active.Remove(number); }
        }
        private PdfObject ReadIndirect(PdfSyntax syntax, PdfValue? expected, CancellationToken token)
        {
            int number = syntax.Read().Integer(), generation = syntax.Read().Integer(); syntax.Expect("obj");
            if (number <= 0 || generation is < 0 or > 65535) { throw new InvalidDataException("Invalid PDF indirect object identity."); }
            if (expected is not null && (number != expected.ObjectNumber || generation != expected.Generation)) { throw new InvalidDataException("PDF indirect object identity mismatch."); }
            PdfValue value = syntax.Read();
            syntax.Skip();
            string next = syntax.Word();
            if (next == "endobj") { return new(value, default); }
            if (next != "stream" || value.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("Invalid PDF indirect object ending."); }
            int position = syntax.Position;
            if (position < _data.Length && _data.Span[position] == 13) { position++; }
            if (position < _data.Length && _data.Span[position] == 10) { position++; }
            else if (position == syntax.Position) { throw new InvalidDataException("Missing PDF stream line ending."); }
            int length = Resolve(value.Get("Length"), token).Integer();
            if (length < 0 || position > _data.Length - length) { throw new InvalidDataException("PDF stream length exceeds the input."); }
            ReadOnlyMemory<byte> stream = _data.Slice(position, length);
            syntax.Position = position + length;
            syntax.Expect("endstream"); syntax.Expect("endobj");
            return new(value, stream, true);
        }
        internal byte[] Decode(PdfObject obj, CancellationToken token, bool image = false)
        {
            if (!obj.HasStream) { throw new InvalidDataException("Expected a PDF stream."); }
            if (_streams.TryGetValue(obj, out byte[]? prior)) { return prior; }
            PdfValue filters = Resolve(obj.Value.Get("Filter"), token), parameters = Resolve(obj.Value.Get("DecodeParms"), token);
            PdfValue[] list = filters.Kind == PdfValueKind.Null ? [] : filters.Kind == PdfValueKind.Array ? filters.Items : [filters];
            if (parameters.Kind == PdfValueKind.Array && parameters.Items.Length != list.Length) { throw new InvalidDataException("PDF filter parameters do not match the filter chain."); }
            ReadOnlyMemory<byte> data = obj.Stream;
            for (int i = 0; i < list.Length; i++)
            {
                string filter = Resolve(list[i], token).Name();
                if (image && filter is "JPXDecode" or "JBIG2Decode" or "DCTDecode")
                { if (i != list.Length - 1) { throw new NotSupportedException("Image codecs must end the PDF filter chain."); } break; }
                PdfValue parameter = parameters.Kind == PdfValueKind.Array ? parameters.Items[i] : parameters;
                data = PdfFilters.Decode(data, filter, Resolve(parameter, token), Budget.Options.MaximumDecodedBytes, token);
            }
            Budget.Decoded(data.Length);
            byte[] result = data.ToArray(); _streams.Add(obj, result); return result;
        }
    }
}
