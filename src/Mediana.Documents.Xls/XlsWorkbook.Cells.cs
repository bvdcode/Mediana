// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Spreadsheets;
using System.Buffers.Binary;

namespace Mediana.Documents.Xls
{
    public partial class XlsWorkbook
    {
        private void AppendCells(BiffRecordReader reader, List<SpreadsheetCell> cells)
        {
            ReadOnlySpan<byte> data = reader.Data.Span;
            int column = CompoundFile.U16(data, 2), style = CompoundFile.U16(data, 4);
            if (reader.Type is BiffRecordType.MulRk or BiffRecordType.MulBlank)
            {
                int width = reader.Type == BiffRecordType.MulRk ? 6 : 2;
                int count = (data.Length - 6) / width;
                if (count == 0 || (data.Length - 6) % width != 0 || CompoundFile.U16(data, data.Length - 2) != column + count - 1)
                { throw new InvalidDataException("Invalid BIFF multiple-cell record."); }
                for (int i = 0; i < count; i++)
                {
                    int offset = 4 + i * width;
                    int format = CompoundFile.U16(data, offset);
                    SpreadsheetValue value = reader.Type switch
                    {
                        BiffRecordType.MulRk => Number(Rk(CompoundFile.U32(data, offset + 2)), format),
                        BiffRecordType.MulBlank => default,
                        _ => throw new InvalidDataException("Unexpected BIFF multiple-cell type.")
                    };
                    Add(new(column + i, value, format));
                }
                return;
            }
            SpreadsheetValue result;
            SpreadsheetFormula? formula = null;
            switch (reader.Type)
            {
                case BiffRecordType.Number:
                    reader.Require(14); result = Number(BinaryPrimitives.ReadDoubleLittleEndian(data[6..]), style); break;
                case BiffRecordType.Rk:
                    reader.Require(10); result = Number(Rk(CompoundFile.U32(data, 6)), style); break;
                case BiffRecordType.LabelSst:
                    reader.Require(10);
                    uint index = CompoundFile.U32(data, 6);
                    if (index >= _strings.Count) { throw new InvalidDataException("XLS shared-string index is outside its table."); }
                    result = SpreadsheetValue.FromText(_strings[(int)index]); break;
                case BiffRecordType.Label:
                    result = SpreadsheetValue.FromText(new BiffTextCursor(reader, 6).ReadString(maximumCharacters: _options.MaximumStringCharacters)); break;
                case BiffRecordType.BoolErr:
                    reader.Require(8);
                    result = data[7] switch
                    {
                        0 => SpreadsheetValue.FromBoolean(Boolean(data[6])),
                        1 => SpreadsheetValue.FromError(Error(data[6])),
                        _ => throw new InvalidDataException("Invalid XLS BoolErr discriminator.")
                    }; break;
                case BiffRecordType.Blank: result = default; break;
                case BiffRecordType.Formula:
                    reader.Require(22);
                    int tokens = CompoundFile.U16(data, 20);
                    if (tokens > data.Length - 22) { throw new InvalidDataException("Truncated BIFF formula tokens."); }
                    formula = new(null, data.Slice(22, tokens).ToArray(), true, Kind: SpreadsheetFormulaKind.Binary)
                    { BinaryExtraData = data[(22 + tokens)..].ToArray() };
                    if (CompoundFile.U16(data, 12) != 0xFFFF) { result = Number(BinaryPrimitives.ReadDoubleLittleEndian(data[6..]), style); }
                    else
                    {
                        byte kind = data[6], cached = data[8];
                        result = kind switch
                        {
                            0 => SpreadsheetValue.FromText(FormulaString(reader)),
                            1 => SpreadsheetValue.FromBoolean(Boolean(cached)),
                            2 => SpreadsheetValue.FromError(Error(cached)),
                            3 => SpreadsheetValue.FromText(""),
                            _ => throw new InvalidDataException("Unknown cached BIFF formula value type.")
                        };
                    }
                    break;
                default: throw new InvalidDataException("Unexpected BIFF cell record.");
            }
            Add(new(column, result, style, formula));

            void Add(SpreadsheetCell cell)
            {
                if (cell.ColumnIndex >= Math.Min(_options.MaximumColumns, 256) || cells.Count != 0 && cell.ColumnIndex <= cells[^1].ColumnIndex)
                { throw new InvalidDataException("XLS cells are not increasing or exceed their column limit."); }
                if (cell.StyleIndex >= _dateStyles.Length) { throw new InvalidDataException("XLS style index is outside its table."); }
                cells.Add(cell);
            }
        }
        private string FormulaString(BiffRecordReader reader)
        {
            if (!reader.MoveNext() || reader.Type != BiffRecordType.String) { throw new InvalidDataException("Missing BIFF formula STRING result."); }
            return new BiffTextCursor(reader, 0).ReadString(maximumCharacters: _options.MaximumStringCharacters);
        }
        private SpreadsheetValue Number(double value, int style)
        {
            if (style >= _dateStyles.Length) { throw new InvalidDataException("XLS style index is outside its table."); }
            return SpreadsheetNumberFormats.Number(value, _dateStyles[style], DateSystem);
        }
        private static bool Boolean(byte value) => value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Invalid XLS Boolean value.")
        };
        private static string Error(byte code) => code switch
        {
            0 => "#NULL!", 7 => "#DIV/0!", 15 => "#VALUE!", 23 => "#REF!", 29 => "#NAME?", 36 => "#NUM!", 42 => "#N/A", 43 => "#GETTING_DATA",
            _ => throw new InvalidDataException("Unknown XLS error code.")
        };
        private static double Rk(uint value)
        {
            double number = (value & 2) != 0 ? (int)value >> 2 : BitConverter.Int64BitsToDouble((long)(value & 0xFFFFFFFC) << 32);
            return (value & 1) != 0 ? number / 100 : number;
        }
    }
}
