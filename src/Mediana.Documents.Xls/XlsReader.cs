// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    /// <summary>Reads CFB/BIFF8 XLS independently of SpreadsheetML and without evaluating formulas.</summary>
    public static class XlsReader
    {
        public static XlsWorkbook Open(ReadOnlyMemory<byte> input, XlsReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new();
            options.Validate(input.Length);
            if (options.MaximumDirectoryEntries <= 0 || options.MaximumSubstreamDepth <= 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
            CompoundFile file = new(input, options, cancellationToken);
            bool completed = false;
            try
            {
                XlsWorkbook workbook = new(file, options, cancellationToken);
                completed = true;
                return workbook;
            }
            finally { if (!completed) { file.Dispose(); } }
        }
    }
}
