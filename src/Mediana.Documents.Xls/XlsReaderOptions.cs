// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Spreadsheets;

namespace Mediana.Documents.Xls
{
    public record XlsReaderOptions : SpreadsheetReaderOptions
    {
        public int MaximumDirectoryEntries { get; init; } = 16_384;
        public int MaximumSubstreamDepth { get; init; } = 32;
    }
}
