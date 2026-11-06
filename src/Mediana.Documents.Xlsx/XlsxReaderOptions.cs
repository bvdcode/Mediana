// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;
using Mediana.Documents.Spreadsheets;

namespace Mediana.Documents.Xlsx
{
    public record XlsxReaderOptions : SpreadsheetReaderOptions
    {
        public OpenXmlPackageOptions Package { get; init; } = new();
    }
}
