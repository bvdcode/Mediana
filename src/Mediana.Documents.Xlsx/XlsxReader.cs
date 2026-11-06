// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.OpenXml;

namespace Mediana.Documents.Xlsx
{
    /// <summary>Opens a SpreadsheetML workbook. Input is borrowed until the workbook is disposed; formulas are never evaluated.</summary>
    public static class XlsxReader
    {
        public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
        public static XlsxWorkbook Open(ReadOnlyMemory<byte> input, XlsxReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            options.Validate(input.Length);
            OpenXmlPackage package = OpenXmlPackage.Open(input, options.Package, cancellationToken);
            bool completed = false;
            try
            {
                XlsxWorkbook workbook = new(package, options, cancellationToken);
                completed = true;
                return workbook;
            }
            finally { if (!completed) { package.Dispose(); } }
        }
    }
}
