// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Spreadsheets;

namespace Mediana.Documents.Xls
{
    public partial class XlsWorkbook : SpreadsheetWorkbook
    {
        private CompoundFile? _file;
        private readonly XlsReaderOptions _options;
        private readonly long[] _positions;
        private readonly IReadOnlyList<string> _strings;
        private readonly bool[] _dateStyles;
        public override IReadOnlyList<SpreadsheetSheet> Sheets { get; }
        public override SpreadsheetDateSystem DateSystem { get; }

        internal XlsWorkbook(CompoundFile file, XlsReaderOptions options, CancellationToken token)
        {
            _file = file;
            _options = options;
            using Stream stream = file.OpenRootStream("Workbook", token);
            using BiffRecordReader reader = new(stream, options.MaximumRecords, token);
            reader.Bof(0x0005);
            List<SpreadsheetSheet> sheets = [];
            List<long> positions = [];
            List<string> strings = [];
            List<int> styles = [];
            Dictionary<int, string> formats = [];
            bool shared = false, eof = false;
            DateSystem = SpreadsheetDateSystem.Excel1900;
            while (reader.MoveNext())
            {
                switch (reader.Type)
                {
                    case BiffRecordType.Eof: eof = true; break;
                    case BiffRecordType.FilePass: throw new NotSupportedException("Encrypted XLS workbooks are not supported.");
                    case BiffRecordType.BoundSheet:
                        reader.Require(8);
                        if (sheets.Count >= options.MaximumSheets) { throw new InvalidDataException("XLS exceeds MaximumSheets."); }
                        long offset = CompoundFile.U32(reader.Data.Span, 0);
                        if (offset >= stream.Length) { throw new InvalidDataException("XLS sheet offset is outside its workbook stream."); }
                        SpreadsheetSheetVisibility visibility = (reader.Data.Span[4] & 3) switch
                        {
                            0 => SpreadsheetSheetVisibility.Visible,
                            1 => SpreadsheetSheetVisibility.Hidden,
                            2 => SpreadsheetSheetVisibility.VeryHidden,
                            _ => throw new InvalidDataException("Invalid XLS sheet visibility.")
                        };
                        SpreadsheetSheetKind kind = reader.Data.Span[5] switch
                        {
                            0 => SpreadsheetSheetKind.Worksheet,
                            1 or 6 => SpreadsheetSheetKind.Macro,
                            2 => SpreadsheetSheetKind.Chart,
                            _ => throw new NotSupportedException("Unknown XLS sheet kind.")
                        };
                        string name = new BiffTextCursor(reader, 6).ReadString(shortLength: true);
                        if (sheets.Any(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        { throw new InvalidDataException("Duplicate XLS sheet name."); }
                        sheets.Add(new(sheets.Count, name, visibility, kind)); positions.Add(offset);
                        break;
                    case BiffRecordType.Date1904:
                        reader.Require(2);
                        DateSystem = CompoundFile.U16(reader.Data.Span, 0) switch
                        {
                            0 => SpreadsheetDateSystem.Excel1900,
                            1 => SpreadsheetDateSystem.Excel1904,
                            _ => throw new InvalidDataException("Invalid XLS date system.")
                        };
                        break;
                    case BiffRecordType.Xf:
                        reader.Require(20);
                        if (styles.Count >= options.MaximumStyles) { throw new InvalidDataException("XLS exceeds MaximumStyles."); }
                        styles.Add(CompoundFile.U16(reader.Data.Span, 2));
                        break;
                    case BiffRecordType.Format:
                        reader.Require(5);
                        int id = CompoundFile.U16(reader.Data.Span, 0);
                        string format = new BiffTextCursor(reader, 2).ReadString();
                        if (formats.Count >= options.MaximumStyles || !formats.TryAdd(id, format)) { throw new InvalidDataException("Duplicate or excessive XLS number formats."); }
                        break;
                    case BiffRecordType.Sst:
                        if (shared) { throw new InvalidDataException("Duplicate BIFF shared-string tables."); }
                        shared = true;
                        ReadStrings(reader, strings);
                        break;
                }
                if (eof) { break; }
            }
            if (!eof) { throw new InvalidDataException("Missing XLS workbook EOF record."); }
            Sheets = sheets.AsReadOnly(); _positions = positions.ToArray(); _strings = strings.AsReadOnly();
            _dateStyles = styles.Count == 0 ? [false] : styles.Select(id => SpreadsheetNumberFormats.IsDateTime(id, formats.GetValueOrDefault(id))).ToArray();
        }

        private void ReadStrings(BiffRecordReader reader, List<string> strings)
        {
            reader.Require(8);
            uint total = CompoundFile.U32(reader.Data.Span, 0), count = CompoundFile.U32(reader.Data.Span, 4);
            if (count > total || count > _options.MaximumSharedStrings) { throw new InvalidDataException("Invalid or excessive BIFF shared-string count."); }
            BiffTextCursor cursor = new(reader, 8);
            long characters = 0;
            for (int i = 0; i < count; i++)
            {
                string text = cursor.ReadString(rich: true, maximumCharacters: _options.MaximumStringCharacters - characters);
                if (text.Length > _options.MaximumStringCharacters - characters) { throw new InvalidDataException("XLS exceeds its shared-string character limit."); }
                characters += text.Length;
                strings.Add(text);
            }
            if (cursor.Remaining != 0) { throw new InvalidDataException("Trailing data in BIFF shared-string table."); }
        }

        public override IEnumerable<SpreadsheetRow> ReadRows(int sheetIndex, CancellationToken cancellationToken = default)
        {
            CompoundFile file = _file ?? throw new ObjectDisposedException(nameof(XlsWorkbook));
            ArgumentOutOfRangeException.ThrowIfNegative(sheetIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sheetIndex, Sheets.Count);
            if (Sheets[sheetIndex].Kind != SpreadsheetSheetKind.Worksheet) { throw new NotSupportedException("The selected XLS sheet is not a worksheet."); }
            using Stream stream = file.OpenRootStream("Workbook", cancellationToken);
            stream.Position = _positions[sheetIndex];
            using BiffRecordReader reader = new(stream, _options.MaximumRecords, cancellationToken);
            reader.Bof(0x0010);
            List<SpreadsheetCell> cells = [];
            int previousRow = -1;
            long characters = 0;
            int depth = 1;
            while (reader.MoveNext())
            {
                if (reader.Type == BiffRecordType.Eof)
                {
                    if (--depth != 0) { continue; }
                    if (cells.Count != 0) { yield return new(previousRow, cells.AsReadOnly()); }
                    yield break;
                }
                if (reader.Type == BiffRecordType.Bof)
                {
                    if (++depth > _options.MaximumSubstreamDepth) { throw new InvalidDataException("XLS exceeds its nested-substream limit."); }
                    continue;
                }
                if (depth != 1) { continue; }
                if (reader.Type == BiffRecordType.FilePass) { throw new NotSupportedException("Encrypted XLS worksheets are not supported."); }
                if (reader.Type is not (BiffRecordType.Number or BiffRecordType.Rk or BiffRecordType.MulRk or BiffRecordType.LabelSst
                    or BiffRecordType.Label or BiffRecordType.BoolErr or BiffRecordType.Blank or BiffRecordType.MulBlank or BiffRecordType.Formula)) { continue; }
                reader.Require(6);
                int row = CompoundFile.U16(reader.Data.Span, 0);
                if (row < previousRow || row >= _options.MaximumRows) { throw new InvalidDataException("XLS rows are not increasing or exceed their limit."); }
                if (row != previousRow && cells.Count != 0)
                { yield return new(previousRow, cells.AsReadOnly()); cells = []; characters = 0; }
                previousRow = row;
                int firstCell = cells.Count;
                bool sharedText = reader.Type == BiffRecordType.LabelSst;
                AppendCells(reader, cells);
                if (!sharedText)
                {
                    for (int i = firstCell; i < cells.Count; i++)
                    { if (cells[i].Value.Kind == SpreadsheetValueKind.Text) { characters += cells[i].Value.Text.Length; } }
                    if (characters > _options.MaximumStringCharacters) { throw new InvalidDataException("XLS row exceeds its string limit."); }
                }
            }
            throw new InvalidDataException("Missing XLS worksheet EOF record.");
        }

        public override void Dispose() { _file?.Dispose(); _file = null; }
    }
}
