// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    public record SpreadsheetReaderOptions
    {
        public int MaximumInputBytes { get; init; } = 128 * 1024 * 1024;
        public int MaximumSheets { get; init; } = 4096;
        public int MaximumRows { get; init; } = 1_048_576;
        public int MaximumColumns { get; init; } = 16_384;
        public int MaximumSharedStrings { get; init; } = 1_000_000;
        public long MaximumStringCharacters { get; init; } = 64L * 1024 * 1024;
        public int MaximumStyles { get; init; } = 65_536;
        public int MaximumRecords { get; init; } = 10_000_000;
        public void Validate(int inputLength)
        {
            if (MaximumInputBytes <= 0 || MaximumSheets <= 0 || MaximumRows <= 0 || MaximumColumns <= 0
                || MaximumSharedStrings <= 0 || MaximumStringCharacters <= 0 || MaximumStyles <= 0 || MaximumRecords <= 0)
            { throw new ArgumentOutOfRangeException(nameof(MaximumInputBytes)); }
            if (inputLength > MaximumInputBytes) { throw new InvalidDataException("Spreadsheet exceeds MaximumInputBytes."); }
        }
    }
}
