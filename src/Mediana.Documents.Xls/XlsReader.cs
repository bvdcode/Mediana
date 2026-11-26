// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Xls
{
    /// <summary>Reads CFB/BIFF8 XLS independently of SpreadsheetML and without evaluating formulas.</summary>
    public static class XlsReader
    {
        /// <summary>Recognizes a CFB container prefix. Opening the container validates its BIFF workbook stream.</summary>
        public static bool HasCompoundFileSignature(ReadOnlySpan<byte> prefix)
        {
            ReadOnlySpan<byte> signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
            return prefix.StartsWith(signature);
        }

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
