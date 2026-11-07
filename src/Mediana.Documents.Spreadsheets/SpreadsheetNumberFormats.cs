// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>Classifies date/time formats without formatting values or interpreting formula expressions.</summary>
    public static class SpreadsheetNumberFormats
    {
        public static bool IsDateTime(int formatId, string? code = null)
        {
            if (code is null) { return formatId is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58; }
            bool quoted = false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = char.ToLowerInvariant(code[i]);
                if (c == '"') { quoted = !quoted; continue; }
                if (quoted) { continue; }
                if (c is '\\' or '_' or '*') { i++; continue; }
                if (c == '[')
                {
                    int end = code.IndexOf(']', i + 1);
                    if (end < 0) { throw new InvalidDataException("Unterminated spreadsheet format bracket."); }
                    ReadOnlySpan<char> field = code.AsSpan(i + 1, end - i - 1);
                    if (!field.IsEmpty && field.IndexOfAnyExcept("hHmMsS") < 0) { return true; }
                    i = end;
                    continue;
                }
                if (c is 'y' or 'd' or 'h' or 'm' or 's') { return true; }
            }
            return false;
        }

        public static SpreadsheetValue Number(double value, bool isDate, SpreadsheetDateSystem system) => isDate
            ? SpreadsheetValue.FromDateTime(new(value, system)) : SpreadsheetValue.FromNumber(value);
    }
}
