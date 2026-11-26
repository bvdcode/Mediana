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
            return OpenWorkbook(package, options, cancellationToken);
        }

        /// <summary>Opens a complete workbook from a borrowed seekable stream. Keep the stream open
        /// and unchanged until disposal. Sheet parts are read on demand.</summary>
        public static XlsxWorkbook Open(Stream input, XlsxReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            options.Validate(0);
            OpenXmlPackageOptions limits = options.Package with
            {
                MaximumInputBytes = Math.Min(options.MaximumInputBytes, options.Package.MaximumInputBytes)
            };
            OpenXmlPackage package = OpenXmlPackage.Open(input, limits, cancellationToken: cancellationToken);
            return OpenWorkbook(package, options, cancellationToken);
        }

        private static XlsxWorkbook OpenWorkbook(OpenXmlPackage package, XlsxReaderOptions options, CancellationToken cancellationToken)
        {
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
